namespace NServiceBus.Features
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Used to configure SLAMonitoring.
    /// </summary>
    public class SLAMonitoring : Feature
    {
        internal SLAMonitoring()
        {
        }

        /// <summary>
        /// <see cref="Feature.Setup" />.
        /// </summary>
        protected internal override void Setup(FeatureConfigurationContext context)
        {
            if (context.Settings.GetOrDefault<bool>("Endpoint.SendOnly"))
            {
                throw new Exception("SLA Monitoring is not supported for send only endpoints, please remove .EnableSLAPerformanceCounter(mySLA) from your config.");
            }

            TimeSpan endpointSla;

            if (!context.Settings.TryGet(EndpointSLAKey, out endpointSla))
            {
                throw new Exception("Endpoint SLA is required for the `SLA violation countdown` counter. Pass the SLA for this endpoint to .EnableSLAPerformanceCounter(mySLA).");
            }

            var counterInstanceName = context.Settings.EndpointName();
            var slaBreachCounter = new EstimatedTimeToSLABreachCounter(endpointSla, counterInstanceName);

            var notifications = context.Settings.Get<NotificationSubscriptions>();

            notifications.Subscribe<ReceivePipelineCompleted>(e =>
            {
                string timeSentString;

                if (!e.ProcessedMessage.Headers.TryGetValue(Headers.TimeSent, out timeSentString))
                {
                    return TaskEx.CompletedTask;
                }

                slaBreachCounter.Update(DateTimeExtensions.ToUtcDateTime(timeSentString), e.StartedAt, e.CompletedAt);

                return TaskEx.CompletedTask;
            });

            context.RegisterStartupTask(() => slaBreachCounter);
        }

        internal const string EndpointSLAKey = "EndpointSLA";

        class EstimatedTimeToSLABreachCounter : FeatureStartupTask
        {
            public EstimatedTimeToSLABreachCounter(TimeSpan endpointSla, string counterInstanceName)
            {
                slaCalculator = new SlaCalculator(endpointSla, UpdateTimeToSLABreach);
                this.counterInstanceName = counterInstanceName;
            }


            public void Update(DateTime sent, DateTime processingStarted, DateTime processingEnded)
            {
                slaCalculator.AddDataPoint(sent, processingStarted, processingEnded);
            }

            protected override Task OnStart(IMessageSession session)
            {
                counter = PerformanceCounterHelper.InstantiatePerformanceCounter("SLA violation countdown", counterInstanceName);
                timer = new Timer(RemoveOldDataPoints, null, 0, 2000);

                return TaskEx.CompletedTask;
            }

            protected override Task OnStop(IMessageSession session)
            {
                timer.Dispose();
                counter.Dispose();

                return TaskEx.CompletedTask;
            }

            void UpdateTimeToSLABreach(int value)
            {
                counter.RawValue = value;
            }

            void RemoveOldDataPoints(object state)
            {
                slaCalculator.RemoveOldDataPoints();
            }

            readonly SlaCalculator slaCalculator;

            PerformanceCounter counter;
            string counterInstanceName;
            // ReSharper disable once NotAccessedField.Local
            Timer timer;
        }

        /// <summary>
        /// The class capturing the SLA calculation algorithm.
        /// </summary>
        public class SlaCalculator
        {
            /// <summary>
            /// Initializes the calculator.
            /// </summary>
            /// <param name="endpointSla"></param>
            /// <param name="reportNewSlaValue"></param>
            public SlaCalculator(TimeSpan endpointSla, Action<int> reportNewSlaValue)
            {
                this.endpointSla = endpointSla;
                this.reportNewSlaValue = reportNewSlaValue;
            }

            /// <summary>
            /// Prunes old data points.
            /// </summary>
            public void RemoveOldDataPoints()
            {
                lock (dataPoints)
                {
                    var last = dataPoints.Count == 0 ? default(DataPoint?) : dataPoints[dataPoints.Count - 1];

                    if (last.HasValue)
                    {
                        var oldestDataToKeep = DateTime.UtcNow - new TimeSpan(last.Value.ProcessingTime.Ticks*3);

                        dataPoints.RemoveAll(d => d.OccurredAt < oldestDataToKeep);
                    }

                    RecalculateSlaValue();
                }
            }

            double CalculateTimeToSLABreach(List<DataPoint> snapshots)
            {
                if (snapshots.Count < 1)
                {
                    return double.MaxValue;
                }

                // as the algorithm for calculating criticalTimeDelta is following
                //
                // (s[1] - s[0]) + (s[2] - s[1]) + ... + (s[n] - s[n-1])
                //
                // it can be grouped that
                //
                // -s[0] + (s[1] - s[1]) + (s[2] - s[2]) + ... + s[n]
                // 
                // which gives
                // s[n] - s[0]

                var last = snapshots.Count - 1;
                var criticalTimeDelta = snapshots[last].CriticalTime - snapshots[0].CriticalTime;

                if (criticalTimeDelta.TotalSeconds <= 0.0)
                {
                    return double.MaxValue;
                }

                var elapsedTime = snapshots[last].OccurredAt - snapshots[0].OccurredAt;

                if (elapsedTime.TotalSeconds <= 0.0)
                {
                    return double.MaxValue;
                }

                var lastKnownCriticalTime = snapshots[last].CriticalTime.TotalSeconds;

                var criticalTimeDeltaPerSecond = criticalTimeDelta.TotalSeconds/elapsedTime.TotalSeconds;

                var secondsToSLABreach = (endpointSla.TotalSeconds - lastKnownCriticalTime)/criticalTimeDeltaPerSecond;

                if (secondsToSLABreach < 0.0)
                {
                    return 0.0;
                }

                return secondsToSLABreach;
            }

            /// <summary>
            /// Adds new data point to the calculator
            /// </summary>
            public void AddDataPoint(DateTime sent, DateTime processingStarted, DateTime processingEnded)
            {
                var dataPoint = new DataPoint
                {
                    CriticalTime = processingEnded - sent,
                    ProcessingTime = processingEnded - processingStarted,
                    OccurredAt = processingEnded
                };

                lock (dataPoints)
                {
                    dataPoints.Add(dataPoint);
                    if (dataPoints.Count > MaxDataPoints)
                    {
                        dataPoints.RemoveRange(0, dataPoints.Count - MaxDataPoints);
                    }

                    RecalculateSlaValue();
                }
            }

            void RecalculateSlaValue()
            {
                var secondsToSLABreach = CalculateTimeToSLABreach(dataPoints);
                var value = Convert.ToInt32((int) Math.Min(secondsToSLABreach, int.MaxValue));
                reportNewSlaValue(value);
            }

            readonly Action<int> reportNewSlaValue;
            List<DataPoint> dataPoints = new List<DataPoint>();
            TimeSpan endpointSla;
            internal const int MaxDataPoints = 10;

            struct DataPoint
            {
                public TimeSpan CriticalTime;
                public DateTime OccurredAt;
                public TimeSpan ProcessingTime;
            }
        }
    }
}