using System;
using System.Threading;
using System.Xml;

namespace  Blogical.Shared.Adapters.Common.Schedules
{
	/// <summary>
	/// Daily Schedule class supporting  Microsoft.Biztalk.Scheduler.ISchedule interface.
	/// Allows scheduling by interval (e.g. every 3 days)  or by  weekday (e.g. on Mondays and Fridays)
	/// </summary>
	[Serializable()]
	public class DaySchedule: Schedule
	{
		//Fields
		private int _interval;					//day interval
		private object _days = 0;				//days of week
		
        // Properties
        /// <summary>
        /// The number of units between polling request
        /// </summary>
		public int Interval 
		{
			get
			{
				return _interval;
			}
			set
			{
				if (ScheduledDays == ScheduleDay.None && value <= 1)
				{
					throw new ArgumentOutOfRangeException(nameof(value), "Must specify scheduled days or interval");
				}
				if (value != Interlocked.Exchange(ref _interval, value))
				{
					FireChangedEvent();
				}
			}
		}
        /// <summary>
        /// Days Unit definition
        /// </summary>
		public ScheduleDay ScheduledDays 
		{
			get
			{
				return (ScheduleDay)_days;
			}
			set
			{
				if (value == ScheduleDay.None && Interval <= 1)
				{
					throw new ArgumentOutOfRangeException(nameof(value), "Must specify scheduled days or interval");
				}
				if (value != (ScheduleDay)Interlocked.Exchange(ref _days, value))
				{
					FireChangedEvent();
				}
			}
		}		
		//Methods
        /// <summary>
        /// Constructor
        /// </summary>
		public DaySchedule()
		{
		}
        /// <summary>
        /// Constructor
        /// </summary>
        public DaySchedule(string configxml)
		{
			XmlDocument configXml = new XmlDocument();
			configXml.LoadXml(configxml);
			Type = ExtractScheduleType(configXml);
			if (Type != ScheduleType.Daily)
			{
				throw new ApplicationException("Invalid Configuration Type");
			}
			StartDate = ExtractDate(configXml, "/schedule/startdate", true);
			StartTime = ExtractTime(configXml, "/schedule/starttime", true);
			
			_interval = IfExistsExtractInt(configXml, "/schedule/interval", 0);
			if (Interval == 0)
			{
				ScheduledDays = ExtractScheduleDay(configXml, "/schedule/days", true);
			}
		}
        /// <summary>
        /// Returns the next time the schedule will be triggerd
        /// </summary>
        /// <returns></returns>
		public override DateTime GetNextActivationTime()
		{
            TraceMessage("[DaySchedule]Executing GetNextActivationTime");
			if (Interval == 0 && ScheduledDays == ScheduleDay.None)
			{
				throw new ApplicationException("Uninitialized daily schedule"); 
			}
			DateTime now = DateTime.Now;
			if (StartDate > now)
			{
				now =  new DateTime(StartDate.Year, StartDate.Month, StartDate.Day, 0, 0,0);
				if (Interval > 1)
				{
					return now;
				}
			}
			//Interval Days
			if (_interval > 0)
			{
				DateTime compare =  new DateTime(now.Year, now.Month, now.Day,0, 0, 0);
				TimeSpan diff = compare.Subtract(StartDate);
				int daysAhead = diff.Days % _interval;
				int daysToGo;
				if (daysAhead == 0)
				{
					if (StartTime.Hour == now.Hour && StartTime.Minute > now.Minute || StartTime.Hour > now.Hour)
					{
						return new DateTime(now.Year, now.Month, now.Day, StartTime.Hour, StartTime.Minute, 0);
					}
					daysToGo = _interval;
				}
				else
				{
					daysToGo = _interval - daysAhead;
				}
				DateTime returnDate = new DateTime(now.Year, now.Month, now.Day , StartTime.Hour, StartTime.Minute, 0);
				return returnDate.AddDays(daysToGo);
			}
			//Day of Week
			if ((GetScheduleDayFlag(now) & ScheduledDays) > 0)
			{ //today could be our lucky day
				if (StartTime.Hour == now.Hour && StartTime.Minute > now.Minute || StartTime.Hour > now.Hour)
				{
					return new DateTime(now.Year, now.Month, now.Day, StartTime.Hour, StartTime.Minute, 0);
				}
			}
			//Find next day
			for (int i = 1; i < 8; i++)
			{
				now = now.AddDays(1);
				if ((GetScheduleDayFlag(now) & ScheduledDays) > 0)
					break;
			}
			return new DateTime(now.Year, now.Month, now.Day, StartTime.Hour, StartTime.Minute, 0);
		}
	}
}
