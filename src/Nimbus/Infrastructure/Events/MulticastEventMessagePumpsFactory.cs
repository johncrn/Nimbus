﻿using System;
using System.Collections.Generic;
using System.Linq;
using Nimbus.Configuration;
using Nimbus.Configuration.Settings;
using Nimbus.Extensions;
using Nimbus.Infrastructure.MessageSendersAndReceivers;
using Nimbus.Infrastructure.RequestResponse;
using Nimbus.InfrastructureContracts;

namespace Nimbus.Infrastructure.Events
{
    internal class MulticastEventMessagePumpsFactory : ICreateComponents
    {
        private readonly IQueueManager _queueManager;
        private readonly ApplicationNameSetting _applicationName;
        private readonly InstanceNameSetting _instanceName;
        private readonly MulticastEventHandlerTypesSetting _multicastEventHandlerTypes;
        private readonly ILogger _logger;
        private readonly IMulticastEventBroker _multicastEventBroker;
        private readonly DefaultBatchSizeSetting _defaultBatchSize;
        private readonly IClock _clock;

        private readonly GarbageMan _garbageMan = new GarbageMan();

        internal MulticastEventMessagePumpsFactory(IQueueManager queueManager,
                                                   ApplicationNameSetting applicationName,
                                                   InstanceNameSetting instanceName,
                                                   MulticastEventHandlerTypesSetting multicastEventHandlerTypes,
                                                   ILogger logger,
                                                   IMulticastEventBroker multicastEventBroker,
                                                   DefaultBatchSizeSetting defaultBatchSize,
                                                   IClock clock)
        {
            _queueManager = queueManager;
            _applicationName = applicationName;
            _instanceName = instanceName;
            _multicastEventHandlerTypes = multicastEventHandlerTypes;
            _logger = logger;
            _multicastEventBroker = multicastEventBroker;
            _defaultBatchSize = defaultBatchSize;
            _clock = clock;
        }

        public IEnumerable<IMessagePump> CreateAll()
        {
            _logger.Debug("Creating multicast event message pumps");

            var eventTypes = _multicastEventHandlerTypes.Value
                                                        .SelectMany(ht => ht.GetGenericInterfacesClosing(typeof (IHandleMulticastEvent<>)))
                                                        .Select(gi => gi.GetGenericArguments().Single())
                                                        .OrderBy(t => t.FullName)
                                                        .Distinct()
                                                        .ToArray();

            foreach (var eventType in eventTypes)
            {
                _logger.Debug("Creating message pump for multicast event type {0}", eventType.Name);

                var topicPath = PathFactory.TopicPathFor(eventType);
                var subscriptionName = String.Format("{0}.{1}", _applicationName, _instanceName);
                var receiver = new NimbusSubscriptionMessageReceiver(_queueManager, topicPath, subscriptionName);
                _garbageMan.Add(receiver);

                var dispatcher = new MulticastEventMessageDispatcher(_multicastEventBroker, eventType);
                _garbageMan.Add(dispatcher);

                var pump = new MessagePump(receiver, dispatcher, _logger, _defaultBatchSize, _clock);
                _garbageMan.Add(pump);

                yield return pump;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~MulticastEventMessagePumpsFactory()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
            _garbageMan.Dispose();
        }
    }
}