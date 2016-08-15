using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;

using ImpruvIT.BatteryMonitor.Domain.Battery;
using ImpruvIT.BatteryMonitor.Domain.Descriptors;

namespace ImpruvIT.BatteryMonitor.Protocols
{
	public interface IBatteryPackAdapter : INotifyPropertyChanged
	{
		BatteryPack Pack { get; }

		Task RecognizeBattery();
		Task UpdateReadings();
		ISubscription SubscribeToUpdates(Action<BatteryPack> notificationConsumer, UpdateFrequency frequency = UpdateFrequency.Normal);

		IEnumerable<ReadingDescriptorGrouping> GetDescriptors();
		event EventHandler DescriptorsChanged;
	}
}