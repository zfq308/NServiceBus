namespace NServiceBus.Core.Tests.Performance.Statistics
{
    using System;
    using System.Collections.Generic;
    using NServiceBus.Features;
    using NUnit.Framework;

    public class SlaCalculatorTests
    {
        [SetUp]
        public void Setup()
        {
            calculator = new SLAMonitoring.SlaCalculator(TimeSpan.FromSeconds(SLA), ReportNewValue);
            values = new List<int>();
            expected = new List<int>();
        }

        [TearDown]
        public void TearDown()
        {
            // prune Any values first
            var count = expected.Count;
            for (var i = 0; i < count; i++)
            {
                if (expected[i] == Any)
                {
                    expected.RemoveAt(i);
                    values.RemoveAt(i);
                    count -= 1;
                    i -= 1;
                }
            }

            CollectionAssert.AreEqual(expected, values);
        }

        [Test]
        public void Should_report_never_when_not_enough_data_points()
        {
            Add(0, 1, 2, NeverReachSLA);
        }

        [Test]
        public void Should_report_differce_between_processingEnd_and_sent_when_processing_is_dragging()
        {
            Add(0, 0, 1, NeverReachSLA);
            Add(0, 1, 2, 3);
            Add(0, 2, 3, 2);
            Add(0, 3, 4, 1);
            Add(0, 4, 5, Now);
        }

        [Test]
        public void Should_report_differce_between_processingEnd_and_sent_when_processing_is_dragging_2()
        {
            Add(0, 0, 1, NeverReachSLA);
            Add(0, 1, 2, 3);
            Add(0, 2, 3, 2);
            Add(0, 3, 4, 1);
            Add(0, 3, 5, Now);
            Add(0, 3, 4, 1);
        }

        [Test]
        public void Should_report_never_when_following_messages_are_handled_within_sla_from_sent_time()
        {
            Add(0, 1, 2, NeverReachSLA);
            Add(1, 2, 3, NeverReachSLA);
            Add(2, 3, 4, NeverReachSLA);
            Add(3, 4, 5, NeverReachSLA);
            Add(4, 5, 6, NeverReachSLA);
        }

        [Test]
        public void Should_report_never_when_last_message_processingEnd_is_equal_to_first()
        {
            Add(0, 1, 3, Any);
            Add(1, 2, 4, Any);
            Add(-3, 3, 3, NeverReachSLA);
        }

        [Test]
        public void Should_not_be_affected_by_long_series_measuing_only_a_tail()
        {
            // try to teach it SLA data
            for (var i = 0; i < SLAMonitoring.SlaCalculator.MaxDataPoints; i++)
            {
                Add(0, 4, 5, Any);
            }

            for (var i = 0; i < SLAMonitoring.SlaCalculator.MaxDataPoints; i++)
            {
                Add(0, 1, 2, Any);
            }

            Should_report_never_when_following_messages_are_handled_within_sla_from_sent_time();
        }

        [Test]
        public void Should_report_now_when_last_message_exceeds_sla()
        {
            Add(0, 1, 2, NeverReachSLA);
            Add(1, 2, 3, NeverReachSLA);

            const int start = 2;
            Add(start, 3, start + 1 + SLA, Now);
        }

        void PruneOldDataPoints(int expectedSecondsToBreachSla)
        {
            calculator.RemoveOldDataPoints();
            expected.Add(expectedSecondsToBreachSla);
        }

        void Add(int sent, int startProcessing, int endProcessing, int expectedSecondsToBreachSla)
        {
            calculator.AddDataPoint(BaseDate.Add(TimeSpan.FromSeconds(sent)), BaseDate.Add(TimeSpan.FromSeconds(startProcessing)), BaseDate.Add(TimeSpan.FromSeconds(endProcessing)));
            expected.Add(expectedSecondsToBreachSla);
        }

        void ReportNewValue(int slaValue)
        {
            values.Add(slaValue);
        }

        SLAMonitoring.SlaCalculator calculator;
        List<int> values;
        List<int> expected;
        const int NeverReachSLA = int.MaxValue;
        const int Now = 0;
        const int Any = -1;
        const int SLA = 5;

        static readonly DateTime BaseDate = new DateTime(2016, 1, 1, 12, 0, 0);
    }
}