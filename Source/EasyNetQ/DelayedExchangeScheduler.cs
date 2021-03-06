﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EasyNetQ.Producer;
using EasyNetQ.SystemMessages;
using EasyNetQ.Topology;

namespace EasyNetQ
{
    public class DelayedExchangeScheduler : IScheduler
    {
        private const string CancellationNotSupportedErrorMessage = "Future messages cancellation is not suuported by Delayed Exchange Scheduler. If you need this functionality, please configure bus to use Scheduler instead.";
        private readonly IAdvancedBus advancedBus;
        private readonly IConventions conventions;
        private readonly IMessageDeliveryModeStrategy messageDeliveryModeStrategy;
        private readonly IPublishExchangeDeclareStrategy publishExchangeDeclareStrategy;
        private readonly ISerializer serializer;
        private readonly ITypeNameSerializer typeNameSerializer;

        public DelayedExchangeScheduler(
            IAdvancedBus advancedBus,
            IConventions conventions,
            IPublishExchangeDeclareStrategy publishExchangeDeclareStrategy,
            IMessageDeliveryModeStrategy messageDeliveryModeStrategy,
            ISerializer serializer,
            ITypeNameSerializer typeNameSerializer)
        {
            Preconditions.CheckNotNull(advancedBus, "advancedBus");
            Preconditions.CheckNotNull(conventions, "conventions");
            Preconditions.CheckNotNull(publishExchangeDeclareStrategy, "publishExchangeDeclareStrategy");
            Preconditions.CheckNotNull(messageDeliveryModeStrategy, "messageDeliveryModeStrategy");
            Preconditions.CheckNotNull(serializer, "serializer");
            Preconditions.CheckNotNull(typeNameSerializer, "typeNameSerializer");

            this.advancedBus = advancedBus;
            this.conventions = conventions;
            this.publishExchangeDeclareStrategy = publishExchangeDeclareStrategy;
            this.messageDeliveryModeStrategy = messageDeliveryModeStrategy;
            this.serializer = serializer;
            this.typeNameSerializer = typeNameSerializer;
        }



        public void FuturePublish<T>(DateTime futurePublishDate, T message) where T : class
        {
            var messageDelay = futurePublishDate.Subtract(DateTime.UtcNow);
            FuturePublishAsync(messageDelay, message).Wait();
        }

        public void FuturePublish<T>(DateTime futurePublishDate, string cancellationKey, T message) where T : class
        {
            if (cancellationKey != null)
                throw new NotImplementedException(CancellationNotSupportedErrorMessage);

            FuturePublish(futurePublishDate, message);
        }

        public void FuturePublish<T>(TimeSpan messageDelay, T message) where T : class
        {
            FuturePublishAsync(messageDelay, message).Wait();
        }

        public void CancelFuturePublish(string cancellationKey)
        {
            throw new NotImplementedException(CancellationNotSupportedErrorMessage);
        }

        public Task FuturePublishAsync<T>(DateTime futurePublishDate, T message) where T : class
        {
            return FuturePublishAsync(futurePublishDate, null, message);
        }

        public Task FuturePublishAsync<T>(DateTime futurePublishDate, string cancellationKey, T message) where T : class
        {
            if (cancellationKey != null)
                throw new NotImplementedException(CancellationNotSupportedErrorMessage);

            return FuturePublishAsync(futurePublishDate, message);
        }

        public Task FuturePublishAsync<T>(TimeSpan messageDelay, T message) where T : class
        {
            Preconditions.CheckNotNull(message, "message");

            var exchangeName = conventions.ExchangeNamingConvention(typeof (T));
            var futureExchangeName = exchangeName + "_delayed";
            var queueName = conventions.QueueNamingConvention(typeof (T), String.Empty);

            return advancedBus.ExchangeDeclareAsync(futureExchangeName, ExchangeType.Direct, delayed: true)
                .Then(futureExchange => advancedBus.ExchangeDeclareAsync(exchangeName, ExchangeType.Topic)
                    .Then(exchange => advancedBus.BindAsync(futureExchange, exchange, "#")
                        .Then(binding => advancedBus.QueueDeclareAsync(queueName)
                            .Then(queue => advancedBus.BindAsync(exchange, queue, "#"))
                            .Then(() =>
                            {
                                var easyNetQMessage = new Message<T>(message)
                                {
                                    Properties =
                                    {
                                        DeliveryMode = messageDeliveryModeStrategy.GetDeliveryMode(typeof (T)),
                                        Headers =
                                            new Dictionary<string, object> {{"x-delay", (int) messageDelay.TotalMilliseconds}}
                                    }
                                };
                                return advancedBus.PublishAsync(futureExchange, "#", false, false, easyNetQMessage);
                            })
                        )
                    )
                );
        }

        public Task CancelFuturePublishAsync(string cancellationKey)
        {
            throw new NotImplementedException(CancellationNotSupportedErrorMessage);
        }
    }
}