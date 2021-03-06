﻿using System;
using System.Collections.Generic;
using System.Linq;
using ImpruvIT.Contracts;

namespace ImpruvIT.BatteryMonitor.Domain.Battery
{
	public class BatteryActualsWrapper : DataDictionaryWrapperBase
	{
		public BatteryActualsWrapper(ReadingStorage data)
			: base(data)
		{
		}

        public ushort BatteryStatus
        {
            get { return this.GetValue<ushort>(BatteryStatusKey); }
            set { this.SetValue(BatteryStatusKey, value); }
        }

        #region Readings

        public float Voltage
		{
			get { return this.GetValue<float>(VoltageKey); }
			set
			{
				Contract.Requires(value, "value").ToBeInRange(x => x >= 0f);

				this.SetValue(VoltageKey, value);
			}
		}

		public float ActualCurrent
		{
			get { return this.GetValue<float>(ActualCurrentKey); }
			set { this.SetValue(ActualCurrentKey, value); }
		}

		public float AverageCurrent
		{
			get { return this.GetValue<float>(AverageCurrentKey); }
			set { this.SetValue(AverageCurrentKey, value); }
		}

		public float Temperature
		{
			get { return this.GetValue<float>(TemperatureKey); }
			set { this.SetValue(TemperatureKey, value); }
		}

        #endregion Readings


        #region Charging request

        public float ChargingVoltage
        {
            get { return this.GetValue<float>(ChargingVoltageKey); }
            set
            {
                Contract.Requires(value, "value").ToBeInRange(x => x >= 0f);

                this.SetValue(ChargingVoltageKey, value);
            }
        }

        public float ChargingCurrent
        {
            get { return this.GetValue<float>(ChargingCurrentKey); }
            set { this.SetValue(ChargingCurrentKey, value); }
        }

        #endregion Charging request


        #region State of charge

        public float RemainingCapacity
		{
			get { return this.GetValue<float>(RemainingCapacityKey); }
			set
			{
				Contract.Requires(value, "value").ToBeInRange(x => x >= 0f);

				this.SetValue(RemainingCapacityKey, value);
			}
		}

		public float AbsoluteStateOfCharge
		{
			get { return this.GetValue<float>(AbsoluteStateOfChargeKey); }
			set
			{
				Contract.Requires(value, "value").ToBeInRange(x => x >= 0f); 
				
				this.SetValue(AbsoluteStateOfChargeKey, value);
			}
		}

		public float RelativeStateOfCharge
		{
			get { return this.GetValue<float>(RelativeStateOfChargeKey); }
			set
			{
				Contract.Requires(value, "value").ToBeInRange(x => 0f <= x && x <= 1f); 
				
				this.SetValue(RelativeStateOfChargeKey, value);
			}
		}

		#endregion State of charge


		#region Run time estimations

		public TimeSpan ActualRunTime
		{
			get { return this.GetValue<TimeSpan>(ActualRunTimeKey); }
			set
			{
				Contract.Requires(value, "value").ToBeInRange(x => x >= TimeSpan.Zero); 
				
				this.SetValue(ActualRunTimeKey, value);
			}
		}

		public TimeSpan AverageRunTime
		{
			get { return this.GetValue<TimeSpan>(AverageRunTimeKey); }
			set
			{
				Contract.Requires(value, "value").ToBeInRange(x => x >= TimeSpan.Zero); 
				
				this.SetValue(AverageRunTimeKey, value);
			}
		}

		#endregion Run time estimations


		#region Entry keys

		private const string NamespaceUriName = "BatteryReadingsNS";

		private const string BatteryStatusEntryName = "BatteryStatus";

		private const string VoltageEntryName = "Voltage";
		private const string ActualCurrentEntryName = "ActualCurrent";
		private const string AverageCurrentEntryName = "AverageCurrent";
		private const string TemperatureEntryName = "Temperature";

        private const string ChargingVoltageEntryName = "ChargingVoltage";
        private const string ChargingCurrentEntryName = "ChargingCurrent";

        private const string RemainingCapacityEntryName = "RemainingCapacity";
		private const string AbsoluteStateOfChargeEntryName = "AbsoluteStateOfCharge";
		private const string RelativeStateOfChargeEntryName = "RelativeStateOfCharge";

		private const string ActualRunTimeEntryName = "ActualRunTime";
		private const string AverageRunTimeEntryName = "AverageRunTime";

		public static readonly EntryKey BatteryStatusKey = CreateKey(BatteryStatusEntryName);

		public static readonly EntryKey VoltageKey = CreateKey(VoltageEntryName);
		public static readonly EntryKey ActualCurrentKey = CreateKey(ActualCurrentEntryName);
		public static readonly EntryKey AverageCurrentKey = CreateKey(AverageCurrentEntryName);
		public static readonly EntryKey TemperatureKey = CreateKey(TemperatureEntryName);

		public static readonly EntryKey ChargingVoltageKey = CreateKey(ChargingVoltageEntryName);
		public static readonly EntryKey ChargingCurrentKey = CreateKey(ChargingCurrentEntryName);

		public static readonly EntryKey RemainingCapacityKey = CreateKey(RemainingCapacityEntryName);
		public static readonly EntryKey AbsoluteStateOfChargeKey = CreateKey(AbsoluteStateOfChargeEntryName);
		public static readonly EntryKey RelativeStateOfChargeKey = CreateKey(RelativeStateOfChargeEntryName);

		public static readonly EntryKey ActualRunTimeKey = CreateKey(ActualRunTimeEntryName);
		public static readonly EntryKey AverageRunTimeKey = CreateKey(AverageRunTimeEntryName);

		private static EntryKey CreateKey(string entryName)
		{
			return new EntryKey(NamespaceUriName, entryName);
		}

		#endregion Entry keys
	}
}
