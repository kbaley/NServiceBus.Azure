using System;
using System.Threading;
using Microsoft.ServiceBus.Messaging;

namespace NServiceBus.Azure.Transports.WindowsAzureServiceBus
{
    using Logging;

    /// <summary>
    /// 
    /// </summary>
    public class AzureServiceBusSubscriptionNotifier : INotifyReceivedMessages
    {
        ILog logger = LogManager.GetLogger(typeof(AzureServiceBusSubscriptionNotifier));

        private SubscriptionClient subscriptionClient;
        private Action<BrokeredMessage> tryProcessMessage;
        private bool cancelRequested;
        Action<Exception> errorProcessingMessage;

        /// <summary>
        /// 
        /// </summary>
        public int ServerWaitTime { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public int BatchSize { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public int BackoffTimeInSeconds { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public ICreateSubscriptionClients SubscriptionClientCreator { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public Type EventType { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="address"></param>
        /// <param name="tryProcessMessage"></param>
        /// <param name="errorProcessingMessage"></param>
        public void Start(Address address, Action<BrokeredMessage> tryProcessMessage, Action<Exception> errorProcessingMessage)
        {
            cancelRequested = false;

            this.tryProcessMessage = tryProcessMessage;
            this.errorProcessingMessage = errorProcessingMessage;

            subscriptionClient = SubscriptionClientCreator.Create(address, EventType);

            if (subscriptionClient != null) subscriptionClient.BeginReceiveBatch(BatchSize, TimeSpan.FromSeconds(ServerWaitTime), OnMessage, null);
        }

        /// <summary>
        /// 
        /// </summary>
        public void Stop()
        {
            cancelRequested = true;
        }

        private void OnMessage(IAsyncResult ar)
        {
            try
            {
                var receivedMessages = subscriptionClient.EndReceiveBatch(ar);

                if (cancelRequested) return;

                foreach (var receivedMessage in receivedMessages)
                {
                    tryProcessMessage(receivedMessage);
                }
            }
            catch (TimeoutException ex)
            {
                logger.Warn(string.Format("Timeout communication exception occured on subscription {0}", subscriptionClient.Name), ex);
                // time's up, just continue and retry
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.Fatal(string.Format("Unauthorized Access Exception occured on subscription {0}", subscriptionClient.Name), ex);

                // errorProcessingMessage(ex);
                // return
                // for now choosen to continue
            }
            catch (MessagingException ex)
            {
                if (cancelRequested)
                {
                    return;
                }

                if (!ex.IsTransient && !RetriableReceiveExceptionHandling.IsRetryable(ex))
                {
                    logger.Fatal(string.Format("{1} Messaging exception occured on subscription {0}", subscriptionClient.Name, (ex.IsTransient ? "Transient" : "Non transient")), ex);

                    errorProcessingMessage(ex);
                }
                else
                {
                    logger.Warn(string.Format("{1} Messaging exception occured on subscription {0}", subscriptionClient.Name, (ex.IsTransient ? "Transient" : "Non transient")), ex);
                }


                logger.Warn("Will retry after backoff period");

                Thread.Sleep(TimeSpan.FromSeconds(BackoffTimeInSeconds));
            }
            finally
            {
                subscriptionClient.BeginReceiveBatch(BatchSize, TimeSpan.FromSeconds(ServerWaitTime), OnMessage, null);
            }
        }
    }
}