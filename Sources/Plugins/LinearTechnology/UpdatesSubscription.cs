﻿using System;
using ImpruvIT.Contracts;
using ImpruvIT.BatteryMonitor.Domain.Battery;

namespace ImpruvIT.BatteryMonitor.Protocols.LinearTechnology
{
	public class UpdatesSubscription : ISubscription
	{
		public UpdatesSubscription(Action<Pack> consumer, UpdateFrequency frequency, Action<UpdatesSubscription> unsubscribeAction)
		{
			Contract.Requires(consumer, "consumer").NotToBeNull();
			Contract.Requires(frequency, "frequency").ToBeDefinedEnumValue();

			this.Consumer = consumer;
			this.Frequency = frequency;
			this.UnsubscribeAction = unsubscribeAction;
		}

		public Action<Pack> Consumer { get; private set; }
		public UpdateFrequency Frequency { get; private set; }
		public Action<UpdatesSubscription> UnsubscribeAction { get; private set; }

		public void Unsubscribe()
		{
			if (this.UnsubscribeAction != null)
				this.UnsubscribeAction(this);
		}

		public void Dispose()
		{
			this.Unsubscribe();
			GC.SuppressFinalize(this);
		}
	}
}
