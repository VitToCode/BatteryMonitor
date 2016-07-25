﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace ImpruvIT.BatteryMonitor.Domain
{
	public partial class ParallelBatteryPack : BatteryPack
	{
		private readonly ParallelPackParameters m_params;
		private readonly ParallelPackHealth m_health;
		private readonly ParallelPackActuals m_actuals;

		public ParallelBatteryPack(IEnumerable<BatteryElement> subElements)
			: base(subElements)
		{
			this.m_params = new ParallelPackParameters(this.SubElements);
			this.m_health = new ParallelPackHealth(this.SubElements);
			this.m_actuals = new ParallelPackActuals(this.SubElements);
		}

		public override IBatteryParameters ProductionParameters
		{
			get { return this.m_params; }
		}

		public override IBatteryHealth Health
		{
			get { return this.m_health; }
		}

		public override IBatteryActuals Actuals
		{
			get { return this.m_actuals; }
		}
	}
}