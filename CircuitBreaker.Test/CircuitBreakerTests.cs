﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CircuitBreaker.Test
{
    [TestClass]
    public class CircuitBreakerTests
    {
        [TestMethod]
        public void Constructor_Empty()
        {
            var cb = new CircuitBreaker();

            Assert.AreEqual(1000, cb.Timeout);
            Assert.AreEqual(5, cb.Threshold);
            Assert.AreEqual(CircuitBreakerState.Closed, cb.State);
        }

        [TestMethod]
        public void Constructor_Overload()
        {
            var cb = new CircuitBreaker(1500, 2);

            Assert.AreEqual(1500, cb.Timeout);
            Assert.AreEqual(2, cb.Threshold);
            Assert.AreEqual(CircuitBreakerState.Closed, cb.State);
        }

        [TestMethod]
        public void CanChangeTimeout()
        {
            var expected = 66;
            var cb = new CircuitBreaker(100, 5);
            cb.Timeout = expected;

            Assert.AreEqual(expected, cb.Timeout);
        }

        [TestMethod]
        public void CanChangeThreshold()
        {
            var expected = 66;
            var cb = new CircuitBreaker(100, 5);
            cb.Threshold = expected;

            Assert.AreEqual(expected, cb.Threshold);
        }

        [TestMethod]
        public void Trip_ChangesStateToOpen()
        {
            var cb = new CircuitBreaker();
            cb.Trip();

            Assert.AreEqual(CircuitBreakerState.Open, cb.State);
        }

        [TestMethod]
        public void Trip_StartsTimer()
        {
            var cb = new CircuitBreaker(250, 2);
            cb.Trip();

            Thread.Sleep(500);

            Assert.AreEqual(CircuitBreakerState.HalfOpen, cb.State);
        }

        [TestMethod]
        public void Reset_ChangesStateToClosed()
        {
            var cb = new CircuitBreaker();
            cb.Trip();
            cb.Reset();

            Assert.AreEqual(CircuitBreakerState.Closed, cb.State);
        }

        [TestMethod]
        public void Timeout_ChangeIncreasesTimerInterval()
        {
            var cb = new CircuitBreaker(500, 3);
            cb.Trip();
            cb.Timeout = 8000;

            // if Timeout does not update _timer.Interval, timer_Elapsed method would be called and state changed to halfopen.
            Thread.Sleep(1000);

            Assert.AreNotEqual(CircuitBreakerState.HalfOpen, cb.State);
        }

        [TestMethod]
        public void Trip_StateChangedEventIsRaised()
        {
            var cb = new CircuitBreaker();
            bool eventRaised = false;
            cb.StateChanged += (s, e) => { eventRaised = true; };
            cb.Trip();

            Assert.IsTrue(eventRaised);
        }

        [TestMethod]
        public void Reset_StateChangedEventIsRaised()
        {
            var cb = new CircuitBreaker();
            bool eventRaised = false;
            cb.StateChanged += (s, e) => { eventRaised = true; };
            cb.Trip();

            Assert.IsTrue(eventRaised);
        }

        [TestMethod]
        [ExpectedException(typeof(OpenCircuitException))]
        public void Execute_OpenStateThrowsException()
        {
            var cb = new CircuitBreaker();
            cb.Trip();
            cb.Execute(new Func<int>(() => { return 1; }));
        }

        [TestMethod]
        public void Execute_CanExecuteOperation()
        {
            var cb = new CircuitBreaker(1000, 3);
            var result = cb.Execute(new Func<int>(() => { return 1 + 2; }));

            Assert.AreEqual(3, result);
            Assert.AreEqual(CircuitBreakerState.Closed, cb.State);
        }

        [TestMethod]
        [ExpectedException(typeof(OperationFailedException))]
        public void Execute_ThrowsExceptionWhenOperationFails()
        {
            Func<int> func = new Func<int>(() => { throw new Exception(); });
            var cb = new CircuitBreaker(1000, 3);
            var result = cb.Execute(func);
        }

        [TestMethod]
        public void Execute_ChangesStateToOpenWhenOperationFails()
        {
            var failureCount = 0;
            var threshold = 1;
            Func<int> failFunc = new Func<int>(() => { throw new Exception(); });
            var cb = new CircuitBreaker(1000, threshold);

            while (failureCount < threshold + 1)
            {
                try
                {
                    var result = cb.Execute(failFunc);
                }
                catch (Exception)
                {
                }

                failureCount++;
            }

            Assert.AreEqual(CircuitBreakerState.Open, cb.State);
        }

        [TestMethod]
        public void Execute_Failure_ChangeStateSequence()
        {
            var failureCount = 0;
            var threshold = 1;
            List<CircuitBreakerState> stateList = new List<CircuitBreakerState>();
            var cb = new CircuitBreaker(500, threshold);
            cb.StateChanged += (s, e) => { stateList.Add(((CircuitBreaker)s).State); };

            while (failureCount < threshold + 1)
            {
                try
                {
                    var result = cb.Execute(new Func<int>(() => { throw new Exception(); }));
                }
                catch (Exception)
                {
                }

                failureCount++;
            }

            Assert.AreEqual(1, stateList.Count);
            Assert.AreEqual(CircuitBreakerState.Open, stateList.Last());
        }

        [TestMethod]
        public void Execute_FailureRetryFailure_ChangeStateSequence()
        {
            var expected = new List<CircuitBreakerState>()
            {
                CircuitBreakerState.Open,
                CircuitBreakerState.HalfOpen,
                CircuitBreakerState.Open
            };
            var failureCount = 0;
            var threshold = 1;
            var failFunc = new Func<int>(() => { throw new Exception(); });
            List<CircuitBreakerState> stateList = new List<CircuitBreakerState>();
            var cb = new CircuitBreaker(500, threshold);
            cb.StateChanged += (s, e) => { stateList.Add(((CircuitBreaker)s).State); };

            while (failureCount < threshold + 1)
            {
                try
                {
                    cb.Execute(failFunc);
                }
                catch (Exception)
                {
                }

                failureCount++;
            }

            Thread.Sleep(1000);

            try
            {
                cb.Execute(failFunc);
            }
            catch (Exception)
            {
            }

            Assert.IsTrue(expected.SequenceEqual(stateList));
            Assert.AreEqual(CircuitBreakerState.Open, stateList.Last());
        }

        [TestMethod]
        public void Execute_FailureRetrySuccess_ChangeStateSequence()
        {
            var expected = new List<CircuitBreakerState>()
            {
                CircuitBreakerState.Open,
                CircuitBreakerState.HalfOpen,
                CircuitBreakerState.Closed
            };
            var failureCount = 0;
            var threshold = 1;
            var failFunc = new Func<int>(() => { throw new Exception(); });
            var successFunc = new Func<int>(() => { return 1; });
            List<CircuitBreakerState> stateList = new List<CircuitBreakerState>();
            var cb = new CircuitBreaker(500, threshold);
            cb.StateChanged += (s, e) => { stateList.Add(((CircuitBreaker)s).State); };

            while (failureCount < threshold + 1)
            {
                try
                {
                    cb.Execute(failFunc);
                }
                catch (Exception)
                {
                }

                failureCount++;
            }

            Thread.Sleep(1000);

            try
            {
                cb.Execute(successFunc);
            }
            catch (Exception)
            {
            }

            Assert.IsTrue(expected.SequenceEqual(stateList));
            Assert.AreEqual(CircuitBreakerState.Closed, stateList.Last());
        }
    }
}
