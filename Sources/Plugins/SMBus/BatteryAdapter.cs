﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

using ImpruvIT.Contracts;
using log4net;

using ImpruvIT.BatteryMonitor.Domain;

namespace ImpruvIT.BatteryMonitor.Protocols.SMBus
{
	public class BatteryAdapter : IBatteryPackAdapter, INotifyPropertyChanged
	{
		private const int RetryCount = 3;

		private readonly object m_lock = new object();
		private readonly RecurrentTask m_monitoringTask;

		public BatteryAdapter(SMBusInterface connection, uint address)
        {
			this.Tracer = LogManager.GetLogger(this.GetType());

			Contract.Requires(connection, "connection").IsNotNull();
			
			this.Connection = connection;
			this.Address = address;
			this.m_monitoringTask = new RecurrentTask(this.MonitoringAction, TimeSpan.FromSeconds(1));
        }

	    protected ILog Tracer { get; private set; }
		protected SMBusInterface Connection { get; private set; }
		protected uint Address { get; private set; }

		public BatteryPack Pack
		{
			get { return this.m_pack; }
			private set
			{
				if (Object.ReferenceEquals(this.m_pack, value))
					return;

				this.m_pack = value;
				this.OnPropertyChanged("Pack");
			}
		}
		private BatteryPack m_pack;


		#region Battery recognition

		public async Task RecognizeBattery()
		{
			this.Tracer.DebugFormat("Recognizing battery at address 0x{0:X} ...", this.Address);

			try
			{
				var pack = await this.RecognizeGeometry().ConfigureAwait(false);
				await this.ReadManufactureData(pack);
				await this.ReadProtocolParams(pack);

				this.Pack = pack;
				this.Tracer.InfoFormat("Battery recognized at address 0x{0:X}: {1} {2} ({3:F2} V, {4:N0} mAh).", this.Address, pack.Product.Manufacturer, pack.Product.Product, pack.ProductionParameters.NominalVoltage, pack.ProductionParameters.DesignedCapacity * 1000);
			}
			catch (Exception ex)
			{
				Exception thrownEx = ex;
				if (thrownEx is AggregateException)
					thrownEx = ((AggregateException)thrownEx).Flatten().InnerException;

				this.Tracer.Warn(String.Format("Error while reading health information of the battery at address 0x{0:X}", this.Address), thrownEx);

				if (!(thrownEx is InvalidOperationException))
					throw;
			}
        }

		private async Task<BatteryPack> RecognizeGeometry()
		{
			var cellCount = (int)(await this.ReadUShortValue(SMBusCommandIds.CellCount).ConfigureAwait(false));
			var nominalVoltage = (await this.ReadUShortValue(SMBusCommandIds.DesignVoltage).ConfigureAwait(false)) / 1000f;
			var cellVoltage = nominalVoltage / cellCount;
			const float designedDischargeCurrent = 0.0f;
			const float maxDischargeCurrent = 0.0f;
			//var designedDischargeCurrent = (await this.ReadUShortValue(SMBusCommandIds.ManufactureDate).ConfigureAwait(false));
			//var maxDischargeCurrent = (await this.ReadUShortValue(SMBusCommandIds.ManufactureDate).ConfigureAwait(false));
			var designedCapacity = (await this.ReadUShortValue(SMBusCommandIds.DesignCapacity).ConfigureAwait(false)) / 1000f;

			var cells = Enumerable.Range(0, cellCount)
				.Select(i => new SingleCell(cellVoltage, designedDischargeCurrent, maxDischargeCurrent, designedCapacity));
			var pack = new SeriesBatteryPack(cells);

			this.Tracer.Debug(new TraceBuilder()
					.AppendLine("A series battery with {1} cells recognized at address 0x{0:X}:", this.Address, cellCount)
					.Indent()
						.AppendLine("Nominal voltage: {0} V (Cell voltage: {1} V)", nominalVoltage, cellVoltage)
						.AppendLine("Designed discharge current: {0} A", pack.ProductionParameters.DesignedDischargeCurrent)
						.AppendLine("Maximal discharge current: {0} A", pack.ProductionParameters.MaxDischargeCurrent)
						.AppendLine("Designed Capacity: {0} Ah", designedCapacity)
					.Trace());

			return pack;
		}

		private async Task ReadManufactureData(BatteryPack pack)
		{
			this.Tracer.DebugFormat("Reading manufacturer data of battery at address 0x{0:X} ...", this.Address);

			var productDefinitionWrapper = new ProductDefinitionWrapper(pack.CustomData);
			productDefinitionWrapper.Manufacturer = await this.ReadStringValue(SMBusCommandIds.ManufacturerName, 16).ConfigureAwait(false);
			productDefinitionWrapper.Product = await this.ReadStringValue(SMBusCommandIds.DeviceName, 16).ConfigureAwait(false);
			productDefinitionWrapper.Chemistry = await this.ReadStringValue(SMBusCommandIds.DeviceChemistry, 5).ConfigureAwait(false);
			productDefinitionWrapper.ManufactureDate = ParseDate(await this.ReadUShortValue(SMBusCommandIds.ManufactureDate).ConfigureAwait(false));
			var serialNumber = await this.ReadUShortValue(SMBusCommandIds.SerialNumber).ConfigureAwait(false);
			productDefinitionWrapper.SerialNumber = serialNumber.ToString(CultureInfo.InvariantCulture);

			this.Tracer.Debug(new TraceBuilder()
					.AppendLine("The manufacturer data of battery at address 0x{0:X}:", this.Address)
					.Indent()
						.AppendLine("Manufacturer:     {0}", pack.Product.Manufacturer)
						.AppendLine("Product:          {0}", pack.Product.Product)
						.AppendLine("Chemistry:        {0}", pack.Product.Chemistry)
						.AppendLine("Manufacture date: {0}", pack.Product.ManufactureDate.ToShortDateString())
						.AppendLine("Serial number:    {0}", pack.Product.SerialNumber)
					.Trace());
		}

		private async Task ReadProtocolParams(BatteryPack pack)
		{
			this.Tracer.DebugFormat("Reading SMBus protocol parameters of the battery at address 0x{0:X} ...", this.Address);

			var batterySMBusWrapper = new SMBusDataWrapper(pack.CustomData);

			// Read SMBus specification info
			var specificationInfo = await this.ReadUShortValue(SMBusCommandIds.SpecificationInfo).ConfigureAwait(false);
			switch (specificationInfo & 0xFF)
			{
			case 0x11:
				batterySMBusWrapper.SpecificationVersion = new Version(1, 0);
				break;

			case 0x21:
				batterySMBusWrapper.SpecificationVersion = new Version(1, 1);
				break;

			case 0x31:
				batterySMBusWrapper.SpecificationVersion = new Version(1, 1, 1);
				break;
			}

			// Read value scales
			batterySMBusWrapper.VoltageScale = (int)Math.Pow(10, (specificationInfo >> 8) & 0x0F);
			batterySMBusWrapper.CurrentScale = (int)Math.Pow(10, (specificationInfo >> 12) & 0x0F);

			this.Tracer.Debug(new TraceBuilder()
				.AppendLine("SMBus protocol parameters of the battery at address 0x{0:X}:", this.Address)
				.Indent()
					.AppendLine("Specification version: {0}", batterySMBusWrapper.SpecificationVersion)
					.AppendLine("Voltage scale: {0}x", batterySMBusWrapper.VoltageScale)
					.AppendLine("Current scale: {0}x", batterySMBusWrapper.CurrentScale)
				.Trace());
		}

		#endregion Battery recognition


		public async Task ReadHealth()
		{
			var pack = this.Pack;
			if (pack == null)
				return;

			this.Tracer.DebugFormat("Reading battery health information of the battery at address 0x{0:X}.", this.Address);

			try
			{
				// Read health information
				var fullChargeCapacity = (await this.ReadUShortValue(SMBusCommandIds.FullChargeCapacity).ConfigureAwait(false)) / 1000f;
				var cycleCount = await this.ReadUShortValue(SMBusCommandIds.CycleCount).ConfigureAwait(false);
				var calculationPrecision = await this.ReadUShortValue(SMBusCommandIds.MaxError).ConfigureAwait(false) / 100f;

				// Read settings
				//status.RemainingCapacityAlarm = (float)await this.ReadUShortValue(SMBusCommandIds.RemainingCapacityAlarm) / 1000;
				//status.RemainingTimeAlarm = TimeSpan.FromMinutes(await this.ReadUShortValue(SMBusCommandIds.RemainingTimeAlarm));
				//status.BatteryMode = await this.ReadUShortValue(SMBusCommandIds.SerialNumber);
				//status.BatteryStatus = await this.ReadUShortValue(SMBusCommandIds.SerialNumber);

				foreach (var cell in pack.SubElements.OfType<SingleCell>())
				{
					cell.SetFullChargeCapacity(fullChargeCapacity);
					cell.SetCycleCount(cycleCount);
					cell.SetCalculationPrecision(calculationPrecision);
				}

				this.Tracer.Debug(new TraceBuilder()
					.AppendLine("The health information of the battery at address 0x{0:X} successfully read:", this.Address)
					.Indent()
						.AppendLine("Full-charge capacity: {0} mAh", pack.Health.FullChargeCapacity * 1000f)
						.AppendLine("Cycle count: {0}", pack.Health.CycleCount)
						.AppendLine("Calculation precision: {0}%", (int)(pack.Health.CalculationPrecision * 100f))
					.Trace());
			}
			catch (Exception ex)
			{
				Exception thrownEx = ex;
				if (thrownEx is AggregateException)
					thrownEx = ((AggregateException)thrownEx).Flatten().InnerException;

				this.Tracer.Warn(String.Format("Error while reading health information of the battery at address 0x{0:X}", this.Address), thrownEx);

				if (!(thrownEx is InvalidOperationException))
					throw;
			}
		}

		public async Task ReadActuals()
		{
			this.Tracer.DebugFormat("Reading battery actuals information of the battery at address 0x{0:X}.", this.Address);

			var pack = this.Pack;
			if (pack == null)
				return;

			try
			{
				//var packVoltage = (await this.ReadUShortValue(SMBusCommandIds.Voltage).ConfigureAwait(false)) / 1000f;
				var actualCurrent = (await this.ReadShortValue(SMBusCommandIds.Current).ConfigureAwait(false)) / 1000f;
				var averageCurrent = (await this.ReadShortValue(SMBusCommandIds.AverageCurrent).ConfigureAwait(false)) / 1000f;
				var temperature = (await this.ReadUShortValue(SMBusCommandIds.Temperature).ConfigureAwait(false)) / 10f;

				var remainingCapacity = (await this.ReadUShortValue(SMBusCommandIds.RemainingCapacity).ConfigureAwait(false)) / 1000f;
				var absoluteStateOfCharge = (await this.ReadUShortValue(SMBusCommandIds.AbsoluteStateOfCharge).ConfigureAwait(false)) / 100f;
				var relativeStateOfCharge = (await this.ReadUShortValue(SMBusCommandIds.RelativeStateOfCharge).ConfigureAwait(false)) / 100f;
				TimeSpan actualRunTime, averageRunTime;
				if (actualCurrent >= 0)
				{
					actualRunTime = TimeSpan.FromMinutes(await this.ReadUShortValue(SMBusCommandIds.RunTimeToEmpty).ConfigureAwait(false));
					averageRunTime = TimeSpan.FromMinutes(await this.ReadUShortValue(SMBusCommandIds.AverageTimeToEmpty).ConfigureAwait(false));
				}
				else
				{
					actualRunTime = TimeSpan.FromMinutes(await this.ReadUShortValue(SMBusCommandIds.AverageTimeToFull).ConfigureAwait(false));
					averageRunTime = actualRunTime;
				}

				//conditions.ChargingVoltage = (await this.ReadUShortValue(SMBusCommandIds.ChargingVoltage).ConfigureAwait(false)) / 1000f;
				//conditions.ChargingCurrent = (await this.ReadUShortValue(SMBusCommandIds.ChargingCurrent).ConfigureAwait(false)) / 1000f;

				var cells = pack.SubElements.OfType<SingleCell>().ToList();
				for (int i = 0; i < cells.Count; i++)
				{
					var cell = cells[i];
					//cell.BeginUpdate();
					//try
					//{

					var cellVoltage = (await this.ReadUShortValue(this.GetCellVoltageCommandId(i)).ConfigureAwait(false)) / 1000f;
					cell.SetVoltage(cellVoltage);
					cell.SetActualCurrent(actualCurrent);
					cell.SetAverageCurrent(averageCurrent);
					cell.SetTemperature(temperature);

					cell.SetRemainingCapacity(remainingCapacity);
					cell.SetAbsoluteStateOfCharge(absoluteStateOfCharge);
					cell.SetRelativeStateOfCharge(relativeStateOfCharge);
					cell.SetActualRunTime(actualRunTime);
					cell.SetAverageRunTime(averageRunTime);

					//}
					//finally
					//{
					//	conditions.EndUpdate();
					//}
				}

				var actuals = pack.Actuals;

				this.Tracer.Debug(new TraceBuilder()
					.AppendLine("The actuals of the battery at address 0x{0:X} successfully read:", this.Address)
					.Indent()
						.AppendLine("Voltage:                 {0} V ({1})", actuals.Voltage, pack.SubElements.Select((c, i) => string.Format("{0}: {1} V", i, c.Actuals.Voltage)).Join(", "))
						.AppendLine("Actual current:          {0} mA", actuals.ActualCurrent * 1000f)
						.AppendLine("Average current:         {0} mA", actuals.AverageCurrent * 1000f)
						.AppendLine("Temperature:             {0:f2} °C", actuals.Temperature - 273.15f)
						.AppendLine("Remaining capacity:      {0:N0} mAh", actuals.RemainingCapacity * 1000f)
						.AppendLine("Absolute StateOfCharge:  {0} %", actuals.AbsoluteStateOfCharge * 100f)
						.AppendLine("Relative StateOfCharge:  {0} %", actuals.RelativeStateOfCharge * 100f)
						.AppendLine("Actual run time:         {0}", actuals.ActualRunTime.ToString())
						.AppendLine("Average run time:        {0}", actuals.AverageRunTime.ToString())
					.Trace());

				
			}
			catch (Exception ex)
			{
				Exception thrownEx = ex;
				if (thrownEx is AggregateException)
					thrownEx = ((AggregateException)thrownEx).Flatten().InnerException;

				this.Tracer.Warn(String.Format("Error while reading actuals of the battery at address 0x{0:X}", this.Address), thrownEx);

				if (!(thrownEx is InvalidOperationException))
					throw;
			}
		}

		private uint GetCellVoltageCommandId(int cellIndex)
		{
			switch (cellIndex)
			{
			case 0: return SMBusCommandIds.CellVoltage1;
			case 1: return SMBusCommandIds.CellVoltage2;
			case 2: return SMBusCommandIds.CellVoltage3;
			case 3: return SMBusCommandIds.CellVoltage4;
			default: throw new ArgumentOutOfRangeException("cellIndex", cellIndex, "Only cells with index 0 to 3 are valid.");
			}
		}


		#region Monitoring

		private const int HealthToActualsRatio = 10;
		private readonly List<UpdatesSubscription> m_subscriptions = new List<UpdatesSubscription>();
		private int m_measurementCount;

		public ISubscription SubscribeToUpdates(Action<BatteryPack> notificationConsumer, UpdateFrequency frequency = UpdateFrequency.Normal)
		{
			lock (this.m_lock)
			{
				var subscription = new UpdatesSubscription(notificationConsumer, frequency, this.UnsubscribeConsumer);
				this.m_subscriptions.Add(subscription);
				this.UpdateMonitoringTask();

				return subscription;
			}
		}

		private void UnsubscribeConsumer(UpdatesSubscription subscription)
		{
			lock (this.m_lock)
			{
				this.m_subscriptions.Remove(subscription);
				this.UpdateMonitoringTask();
			}
		}

		private void UpdateMonitoringTask()
		{
			// Method has to be called within lock

			var hasSubscribers = this.m_subscriptions.Any();
			if (hasSubscribers && !this.m_monitoringTask.IsRunning)
			{
				this.m_measurementCount = 0;
				this.m_monitoringTask.Start();
				this.Tracer.InfoFormat("Monitoring of the battery at address 0x{0:X} started.", this.Address);
			}
			else if (!hasSubscribers && this.m_monitoringTask.IsRunning)
			{
				this.m_monitoringTask.Stop();
				this.Tracer.InfoFormat("Monitoring of the battery at address 0x{0:X} stopped.", this.Address);
			}
				

			// TODO: update periode
		}

		private void MonitoringAction()
		{
			// Read values
			if ((this.m_measurementCount++ % HealthToActualsRatio) == 0)
				this.ReadHealth().Wait();
			this.ReadActuals().Wait();

			List<Task> tasks;
			lock (this.m_lock)
			{
				tasks = this.m_subscriptions.Select(x => x.Consumer).Select(x => Task.Factory.StartNew(() => x(this.Pack))).ToList();
			}

			Task.WhenAll(tasks).ContinueWith(this.ConsumerErrorHandler, TaskContinuationOptions.OnlyOnFaulted);
		}

		private void ConsumerErrorHandler(Task task)
		{
			var ex = task.Exception;
			this.Tracer.Warn("Update notification consumer failed.", ex);

			// Exception intentionally swallowed
		}

		#endregion Monitoring


		//#region Settings

		///// <summary>
		///// Setup battery to report alarm when remaining capacity is lower than set values.
		///// </summary>
		///// <param name="remainingCapacity">Remaining capacity treshold (in Ah); <i>0</i> to disable the alarm; <i>null</i> not to change the threshold.</param>
		///// <param name="remainingTime">Remaining time treshold; <i>zero timespan</i> to disable the alarm; <i>null</i> not to change the threshold.</param>
		//public void SetupRemainingCapacityAlarm(float? remainingCapacity, TimeSpan? remainingTime)
		//{
		//	if (remainingCapacity.HasValue)
		//	{
		//		remainingCapacity = 1000 * remainingCapacity.Value / this.Battery.Information.CurrentScale;
		//		this.Connection.WriteWordCommand(this.Address, SMBusCommandIds.RemainingCapacityAlarm, (ushort)remainingCapacity.Value);
		//	}

		//	if (remainingTime.HasValue)
		//	{
		//		this.Connection.WriteWordCommand(this.Address, SMBusCommandIds.RemainingTimeAlarm, (ushort)remainingTime.Value.TotalMinutes);
		//	}

		//	this.Tracer.InfoFormat(
		//		"Alarm settings for battery at {0:X} to {1} mAh or {2} remaining time.", 
		//		this.Address,
		//		(remainingCapacity.HasValue ? (remainingCapacity.Value * 1000).ToString("N0") : "<No change>"),
		//		(remainingTime.HasValue ? remainingTime.Value.ToString() : "<No change>"));
		//}

		//#endregion Settings

		#region Reading primitives

		private Task<short> ReadShortValue(uint commandId)
		{
			return this.ReadUShortValue(commandId)
				.ContinueWith(t =>
				{
					ushort value = t.Result;
					var bytes = new byte[2];
					bytes[0] = (byte)(value & 0xFF);
					bytes[1] = (byte)((value >> 8) & 0xFF);

					return BitConverter.ToInt16(bytes, 0);
				}, TaskContinuationOptions.OnlyOnRanToCompletion);
		}

		private Task<ushort> ReadUShortValue(uint commandId)
		{
			return this.RetryOperation(() => this.Connection.ReadWordCommand(this.Address, commandId));
		}

		private Task<string> ReadStringValue(uint commandId, int stringLength)
		{
			return this.ReadBlockCommand(commandId, stringLength)
				.ContinueWith(t => Encoding.ASCII.GetString(t.Result), TaskContinuationOptions.OnlyOnRanToCompletion);
		}

		private Task<byte[]> ReadBlockCommand(uint commandId, int blockSize)
		{
			return this.RetryOperation(() => this.Connection.ReadBlockCommand(this.Address, commandId, blockSize));
		}

		private async Task<T> RetryOperation<T>(Func<Task<T>> action, int retryCount = RetryCount)
		{
			int retry = 0;
			while (true)
			{
				var task = action();
				try
				{
					T value = await task;
					return value;
				}
				catch (AggregateException ex)
				{
					if (ex.InnerExceptions.OfType<InvalidOperationException>().Any())
					{
						if (retry == retryCount)
							throw;

						retry++;
					}
					else
						throw;
				}
			}
		}

		private static DateTime ParseDate(ushort packedValue)
		{
			int day = packedValue & 0x1F;
			int month = (packedValue >> 5) & 0x0F;
			int year = 1980 + ((packedValue >> 9) & 0x7F);

			if (day < 1 || day > 31 || month < 1 || month > 12)
				return default(DateTime);

			return new DateTime(year, month, day);
		}

		#endregion Reading primitives

		/// <inheritdoc />
		public event PropertyChangedEventHandler PropertyChanged;

		/// <summary>
		/// Fires the <see cref="PropertyChanged"/> event.
		/// </summary>
		/// <param name="propertyName">The name of the chnaged property.</param>
		protected virtual void OnPropertyChanged(string propertyName)
		{
			PropertyChangedEventHandler handlers = this.PropertyChanged;
			if (handlers != null)
				handlers(this, new PropertyChangedEventArgs(propertyName));
		}
    }
}