﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information. 

#if WINDOWS
using System.Reactive.Disposables;
using Windows.System.Threading;

namespace System.Reactive.Concurrency
{
    /// <summary>
    /// Represents an object that schedules units of work on the Windows Runtime thread pool.
    /// </summary>
    /// <seealso cref="ThreadPoolScheduler.Default">Singleton instance of this type exposed through this static property.</seealso>
    [CLSCompliant(false)]
    public sealed class ThreadPoolScheduler : LocalScheduler, ISchedulerPeriodic
    {
        private static Lazy<ThreadPoolScheduler> s_default = new Lazy<ThreadPoolScheduler>(() => new ThreadPoolScheduler());

        /// <summary>
        /// Constructs a ThreadPoolScheduler that schedules units of work on the Windows ThreadPool.
        /// </summary>
        public ThreadPoolScheduler()
        {
        }

        /// <summary>
        /// Constructs a ThreadPoolScheduler that schedules units of work on the Windows ThreadPool with the given priority.
        /// </summary>
        /// <param name="priority">Priority for scheduled units of work.</param>
        public ThreadPoolScheduler(WorkItemPriority priority)
        {
            Priority = priority;
            Options = WorkItemOptions.None;
        }

        /// <summary>
        /// Constructs a ThreadPoolScheduler that schedules units of work on the Windows ThreadPool with the given priority.
        /// </summary>
        /// <param name="priority">Priority for scheduled units of work.</param>
        /// <param name="options">Options that configure how work is scheduled.</param>
        public ThreadPoolScheduler(WorkItemPriority priority, WorkItemOptions options)
        {
            Priority = priority;
            Options = options;
        }

        /// <summary>
        /// Gets the singleton instance of the Windows Runtime thread pool scheduler.
        /// </summary>
        public static ThreadPoolScheduler Default => s_default.Value;

        /// <summary>
        /// Gets the priority at which work is scheduled.
        /// </summary>
        public WorkItemPriority Priority { get; }

        /// <summary>
        /// Gets the options that configure how work is scheduled.
        /// </summary>
        public WorkItemOptions Options { get; }

        /// <summary>
        /// Schedules an action to be executed.
        /// </summary>
        /// <typeparam name="TState">The type of the state passed to the scheduled action.</typeparam>
        /// <param name="state">State passed to the action to be executed.</param>
        /// <param name="action">Action to be executed.</param>
        /// <returns>The disposable object used to cancel the scheduled action (best effort).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
        public override IDisposable Schedule<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var d = new SingleAssignmentDisposable();

            var res = global::Windows.System.Threading.ThreadPool.RunAsync(iaa =>
            {
                if (!d.IsDisposed)
                {
                    d.Disposable = action(this, state);
                }
            }, Priority, Options);

            return new CompositeDisposable(
                d,
                Disposable.Create(res.Cancel)
            );
        }

        /// <summary>
        /// Schedules an action to be executed after dueTime, using a Windows.System.Threading.ThreadPoolTimer object.
        /// </summary>
        /// <typeparam name="TState">The type of the state passed to the scheduled action.</typeparam>
        /// <param name="state">State passed to the action to be executed.</param>
        /// <param name="action">Action to be executed.</param>
        /// <param name="dueTime">Relative time after which to execute the action.</param>
        /// <returns>The disposable object used to cancel the scheduled action (best effort).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
        public override IDisposable Schedule<TState>(TState state, TimeSpan dueTime, Func<IScheduler, TState, IDisposable> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var dt = Scheduler.Normalize(dueTime);

            if (dt.Ticks == 0)
            {
                return Schedule(state, action);
            }

            return ScheduleSlow(state, dt, action);
        }

        private IDisposable ScheduleSlow<TState>(TState state, TimeSpan dueTime, Func<IScheduler, TState, IDisposable> action)
        {
            var d = new SingleAssignmentDisposable();

            var res = global::Windows.System.Threading.ThreadPoolTimer.CreateTimer(
                tpt =>
                {
                    if (!d.IsDisposed)
                    {
                        d.Disposable = action(this, state);
                    }
                },
                dueTime
            );

            return new CompositeDisposable(
                d,
                Disposable.Create(res.Cancel)
            );
        }

        /// <summary>
        /// Schedules a periodic piece of work, using a Windows.System.Threading.ThreadPoolTimer object.
        /// </summary>
        /// <typeparam name="TState">The type of the state passed to the scheduled action.</typeparam>
        /// <param name="state">Initial state passed to the action upon the first iteration.</param>
        /// <param name="period">Period for running the work periodically.</param>
        /// <param name="action">Action to be executed, potentially updating the state.</param>
        /// <returns>The disposable object used to cancel the scheduled recurring action (best effort).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="period"/> is less than one millisecond.</exception>
        public IDisposable SchedulePeriodic<TState>(TState state, TimeSpan period, Func<TState, TState> action)
        {
            //
            // The WinRT thread pool is based on the Win32 thread pool and cannot handle
            // sub-1ms resolution. When passing a lower period, we get single-shot
            // timer behavior instead. See MSDN documentation for CreatePeriodicTimer
            // for more information.
            //
            if (period < TimeSpan.FromMilliseconds(1))
                throw new ArgumentOutOfRangeException(nameof(period), Strings_PlatformServices.WINRT_NO_SUB1MS_TIMERS);
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            return new PeriodicallyScheduledWorkItem<TState>(state, period, action);
        }

        private sealed class PeriodicallyScheduledWorkItem<TState> : IDisposable
        {
            private TState _state;
            private Func<TState, TState> _action;

            private readonly ThreadPoolTimer _timer;
            private readonly AsyncLock _gate = new AsyncLock();

            public PeriodicallyScheduledWorkItem(TState state, TimeSpan period, Func<TState, TState> action)
            {
                _state = state;
                _action = action;

                _timer = global::Windows.System.Threading.ThreadPoolTimer.CreatePeriodicTimer(
                    Tick,
                    period);
            }

            private void Tick(ThreadPoolTimer timer)
            {
                _gate.Wait(
                    this,
                    @this => @this._state = @this._action(@this._state));
            }

            public void Dispose()
            {
                _timer.Cancel();
                _gate.Dispose();
                _action = Stubs<TState>.I;
            }
        }
    }
}
#endif
