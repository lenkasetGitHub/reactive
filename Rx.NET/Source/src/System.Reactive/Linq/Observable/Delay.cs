﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information. 

using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Threading;

namespace System.Reactive.Linq.ObservableImpl
{
    internal static class Delay<TSource>
    {
        internal abstract class Base<TParent> : Producer<TSource, Base<TParent>._>
            where TParent : Base<TParent>
        {
            protected readonly IObservable<TSource> _source;
            protected readonly IScheduler _scheduler;

            public Base(IObservable<TSource> source, IScheduler scheduler)
            {
                _source = source;
                _scheduler = scheduler;
            }

            internal abstract class _ : IdentitySink<TSource>
            {
                public _(IObserver<TSource> observer)
                    : base(observer)
                {
                }

                public abstract void Run(TParent parent);
            }

            internal abstract class S : _
            {
                protected readonly object _gate = new object();
                protected IDisposable _cancelable;

                protected readonly IScheduler _scheduler;

                public S(TParent parent, IObserver<TSource> observer)
                    : base(observer)
                {
                    _scheduler = parent._scheduler;
                }

                private IDisposable _sourceSubscription;

                protected IStopwatch _watch;
                protected TimeSpan _delay;
                protected bool _ready;
                protected bool _active;
                protected bool _running;
                protected Queue<System.Reactive.TimeInterval<TSource>> _queue = new Queue<Reactive.TimeInterval<TSource>>();

                private bool _hasCompleted;
                private TimeSpan _completeAt;
                private bool _hasFailed;
                private Exception _exception;

                public override void Run(TParent parent)
                {
                    _active = false;
                    _running = false;
                    _queue = new Queue<System.Reactive.TimeInterval<TSource>>();
                    _hasCompleted = false;
                    _completeAt = default(TimeSpan);
                    _hasFailed = false;
                    _exception = default(Exception);

                    _watch = _scheduler.StartStopwatch();

                    RunCore(parent);

                    Disposable.SetSingle(ref _sourceSubscription, parent._source.SubscribeSafe(this));
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing)
                    {
                        Disposable.TryDispose(ref _sourceSubscription);
                        Disposable.TryDispose(ref _cancelable);
                    }

                    base.Dispose(disposing);
                }

                protected abstract void RunCore(TParent parent);

                public override void OnNext(TSource value)
                {
                    var shouldRun = false;

                    lock (_gate)
                    {
                        var next = _watch.Elapsed.Add(_delay);

                        _queue.Enqueue(new System.Reactive.TimeInterval<TSource>(value, next));

                        shouldRun = _ready && !_active;
                        _active = true;
                    }

                    if (shouldRun)
                    {
                        Disposable.TrySetSerial(ref _cancelable, _scheduler.Schedule(this, _delay, (@this, a) => @this.DrainQueue(a)));
                    }
                }

                public override void OnError(Exception error)
                {
                    Disposable.TryDispose(ref _sourceSubscription);

                    var shouldRun = false;

                    lock (_gate)
                    {
                        _queue.Clear();

                        _exception = error;
                        _hasFailed = true;

                        shouldRun = !_running;
                    }

                    if (shouldRun)
                    {
                        ForwardOnError(error);
                    }
                }

                public override void OnCompleted()
                {
                    Disposable.TryDispose(ref _sourceSubscription);

                    var shouldRun = false;

                    lock (_gate)
                    {
                        var next = _watch.Elapsed.Add(_delay);

                        _completeAt = next;
                        _hasCompleted = true;

                        shouldRun = _ready && !_active;
                        _active = true;
                    }

                    if (shouldRun)
                    {
                        Disposable.TrySetSerial(ref _cancelable, _scheduler.Schedule(this, _delay, (@this, a) => @this.DrainQueue(a)));
                    }
                }

                protected void DrainQueue(Action<S, TimeSpan> recurse)
                {
                    lock (_gate)
                    {
                        if (_hasFailed)
                            return;
                        _running = true;
                    }

                    //
                    // The shouldYield flag was added to address TFS 487881: "Delay can be unfair". In the old
                    // implementation, the loop below kept running while there was work for immediate dispatch,
                    // potentially causing a long running work item on the target scheduler. With the addition
                    // of long-running scheduling in Rx v2.0, we can check whether the scheduler supports this
                    // interface and perform different processing (see LongRunningImpl). To reduce the code 
                    // churn in the old loop code here, we set the shouldYield flag to true after the first 
                    // dispatch iteration, in order to break from the loop and enter the recursive scheduling path.
                    //
                    var shouldYield = false;

                    while (true)
                    {
                        var hasFailed = false;
                        var error = default(Exception);

                        var hasValue = false;
                        var value = default(TSource);
                        var hasCompleted = false;

                        var shouldRecurse = false;
                        var recurseDueTime = default(TimeSpan);

                        lock (_gate)
                        {
                            if (_hasFailed)
                            {
                                error = _exception;
                                hasFailed = true;
                                _running = false;
                            }
                            else
                            {
                                var now = _watch.Elapsed;

                                if (_queue.Count > 0)
                                {
                                    var nextDue = _queue.Peek().Interval;

                                    if (nextDue.CompareTo(now) <= 0 && !shouldYield)
                                    {
                                        value = _queue.Dequeue().Value;
                                        hasValue = true;
                                    }
                                    else
                                    {
                                        shouldRecurse = true;
                                        recurseDueTime = Scheduler.Normalize(nextDue.Subtract(now));
                                        _running = false;
                                    }
                                }
                                else if (_hasCompleted)
                                {
                                    if (_completeAt.CompareTo(now) <= 0 && !shouldYield)
                                    {
                                        hasCompleted = true;
                                    }
                                    else
                                    {
                                        shouldRecurse = true;
                                        recurseDueTime = Scheduler.Normalize(_completeAt.Subtract(now));
                                        _running = false;
                                    }
                                }
                                else
                                {
                                    _running = false;
                                    _active = false;
                                }
                            }
                        } /* lock (_gate) */

                        if (hasValue)
                        {
                            ForwardOnNext(value);
                            shouldYield = true;
                        }
                        else
                        {
                            if (hasCompleted)
                            {
                                ForwardOnCompleted();
                            }
                            else if (hasFailed)
                            {
                                ForwardOnError(error);
                            }
                            else if (shouldRecurse)
                            {
                                recurse(this, recurseDueTime);
                            }

                            return;
                        }
                    } /* while (true) */
                }
            }

            protected abstract class L : _
            {
                protected readonly object _gate = new object();
                protected IDisposable _cancelable;
                private readonly SemaphoreSlim _evt = new SemaphoreSlim(0);

                private readonly IScheduler _scheduler;

                public L(TParent parent, IObserver<TSource> observer)
                    : base(observer)
                {
                    _scheduler = parent._scheduler;
                }

                private IDisposable _sourceSubscription;

                protected IStopwatch _watch;
                protected TimeSpan _delay;
                protected Queue<System.Reactive.TimeInterval<TSource>> _queue;

                private CancellationTokenSource _stop;
                private bool _hasCompleted;
                private TimeSpan _completeAt;
                private bool _hasFailed;
                private Exception _exception;

                public override void Run(TParent parent)
                {
                    _queue = new Queue<System.Reactive.TimeInterval<TSource>>();
                    _hasCompleted = false;
                    _completeAt = default(TimeSpan);
                    _hasFailed = false;
                    _exception = default(Exception);

                    _watch = _scheduler.StartStopwatch();

                    RunCore(parent);

                    Disposable.SetSingle(ref _sourceSubscription, parent._source.SubscribeSafe(this));
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing)
                    {
                        Disposable.TryDispose(ref _sourceSubscription);
                        Disposable.TryDispose(ref _cancelable);
                    }
                    base.Dispose(disposing);
                }

                protected abstract void RunCore(TParent parent);

                protected void ScheduleDrain()
                {
                    _stop = new CancellationTokenSource();
                    Disposable.TrySetSerial(ref _cancelable, new CancellationDisposable(_stop));

                    _scheduler.AsLongRunning().ScheduleLongRunning(DrainQueue);
                }

                public override void OnNext(TSource value)
                {

                    lock (_gate)
                    {
                        var next = _watch.Elapsed.Add(_delay);

                        _queue.Enqueue(new System.Reactive.TimeInterval<TSource>(value, next));

                        _evt.Release();
                    }
                }

                public override void OnError(Exception error)
                {
                    Disposable.TryDispose(ref _sourceSubscription);

                    lock (_gate)
                    {
                        _queue.Clear();

                        _exception = error;
                        _hasFailed = true;

                        _evt.Release();
                    }
                }

                public override void OnCompleted()
                {
                    Disposable.TryDispose(ref _sourceSubscription);


                    lock (_gate)
                    {
                        var next = _watch.Elapsed.Add(_delay);

                        _completeAt = next;
                        _hasCompleted = true;

                        _evt.Release();
                    }
                }

                private void DrainQueue(ICancelable cancel)
                {
                    while (true)
                    {
                        try
                        {
                            _evt.Wait(_stop.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }

                        var hasFailed = false;
                        var error = default(Exception);

                        var hasValue = false;
                        var value = default(TSource);
                        var hasCompleted = false;

                        var shouldWait = false;
                        var waitTime = default(TimeSpan);

                        lock (_gate)
                        {
                            if (_hasFailed)
                            {
                                error = _exception;
                                hasFailed = true;
                            }
                            else
                            {
                                var now = _watch.Elapsed;

                                if (_queue.Count > 0)
                                {
                                    var next = _queue.Dequeue();

                                    hasValue = true;
                                    value = next.Value;

                                    var nextDue = next.Interval;
                                    if (nextDue.CompareTo(now) > 0)
                                    {
                                        shouldWait = true;
                                        waitTime = Scheduler.Normalize(nextDue.Subtract(now));
                                    }
                                }
                                else if (_hasCompleted)
                                {
                                    hasCompleted = true;

                                    if (_completeAt.CompareTo(now) > 0)
                                    {
                                        shouldWait = true;
                                        waitTime = Scheduler.Normalize(_completeAt.Subtract(now));
                                    }
                                }
                            }
                        } /* lock (_gate) */

                        if (shouldWait)
                        {
                            var timer = new ManualResetEventSlim();
                            _scheduler.Schedule(timer, waitTime, (_, slimTimer) => { slimTimer.Set(); return Disposable.Empty; });

                            try
                            {
                                timer.Wait(_stop.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                return;
                            }
                        }

                        if (hasValue)
                        {
                            ForwardOnNext(value);
                        }
                        else
                        {
                            if (hasCompleted)
                            {
                                ForwardOnCompleted();
                            }
                            else if (hasFailed)
                            {
                                ForwardOnError(error);
                            }

                            return;
                        }
                    }
                }
            }
        }

        internal sealed class Absolute : Base<Absolute>
        {
            private readonly DateTimeOffset _dueTime;

            public Absolute(IObservable<TSource> source, DateTimeOffset dueTime, IScheduler scheduler)
                : base(source, scheduler)
            {
                _dueTime = dueTime;
            }

            protected override _ CreateSink(IObserver<TSource> observer) => _scheduler.AsLongRunning() != null ? (Base<Absolute>._)new L(this, observer) : new S(this, observer);

            protected override void Run(_ sink) => sink.Run(this);

            new private sealed class S : Base<Absolute>.S
            {
                public S(Absolute parent, IObserver<TSource> observer)
                    : base(parent, observer)
                {
                }

                protected override void RunCore(Absolute parent)
                {
                    _ready = false;

                    Disposable.TrySetSingle(ref _cancelable, parent._scheduler.Schedule(parent._dueTime, Start));
                }

                private void Start()
                {
                    var next = default(TimeSpan);
                    var shouldRun = false;

                    lock (_gate)
                    {
                        _delay = _watch.Elapsed;

                        var oldQueue = _queue;
                        _queue = new Queue<Reactive.TimeInterval<TSource>>();

                        if (oldQueue.Count > 0)
                        {
                            next = oldQueue.Peek().Interval;

                            while (oldQueue.Count > 0)
                            {
                                var item = oldQueue.Dequeue();
                                _queue.Enqueue(new Reactive.TimeInterval<TSource>(item.Value, item.Interval.Add(_delay)));
                            }

                            shouldRun = true;
                            _active = true;
                        }

                        _ready = true;
                    }

                    if (shouldRun)
                    {
                        Disposable.TrySetSerial(ref _cancelable, _scheduler.Schedule((Base<Absolute>.S)this, next, (@this, a) => DrainQueue(a)));
                    }
                }
            }

            new private sealed class L : Base<Absolute>.L
            {
                public L(Absolute parent, IObserver<TSource> observer)
                    : base(parent, observer)
                {
                }

                protected override void RunCore(Absolute parent)
                {
                    // ScheduleDrain might have already set a newer disposable
                    // using TrySetSerial would cancel it, stopping the emission
                    // and hang the consumer
                    Disposable.TrySetSingle(ref _cancelable, parent._scheduler.Schedule(parent._dueTime, Start));
                }

                private void Start()
                {
                    lock (_gate)
                    {
                        _delay = _watch.Elapsed;

                        var oldQueue = _queue;
                        _queue = new Queue<Reactive.TimeInterval<TSource>>();

                        while (oldQueue.Count > 0)
                        {
                            var item = oldQueue.Dequeue();
                            _queue.Enqueue(new Reactive.TimeInterval<TSource>(item.Value, item.Interval.Add(_delay)));
                        }
                    }

                    ScheduleDrain();
                }
            }
        }

        internal sealed class Relative : Base<Relative>
        {
            private readonly TimeSpan _dueTime;

            public Relative(IObservable<TSource> source, TimeSpan dueTime, IScheduler scheduler)
                : base(source, scheduler)
            {
                _dueTime = dueTime;
            }

            protected override _ CreateSink(IObserver<TSource> observer) => _scheduler.AsLongRunning() != null ? (Base<Relative>._)new L(this, observer) : new S(this, observer);

            protected override void Run(_ sink) => sink.Run(this);

            new private sealed class S : Base<Relative>.S
            {
                public S(Relative parent, IObserver<TSource> observer)
                    : base(parent, observer)
                {
                }

                protected override void RunCore(Relative parent)
                {
                    _ready = true;

                    _delay = Scheduler.Normalize(parent._dueTime);
                }
            }

            new private sealed class L : Base<Relative>.L
            {
                public L(Relative parent, IObserver<TSource> observer)
                    : base(parent, observer)
                {
                }

                protected override void RunCore(Relative parent)
                {
                    _delay = Scheduler.Normalize(parent._dueTime);
                    ScheduleDrain();
                }
            }
        }
    }

    internal static class Delay<TSource, TDelay>
    {
        internal abstract class Base<TParent> : Producer<TSource, Base<TParent>._>
            where TParent : Base<TParent>
        {
            protected readonly IObservable<TSource> _source;

            public Base(IObservable<TSource> source)
            {
                _source = source;
            }

            internal abstract class _ : IdentitySink<TSource>
            {
                private readonly CompositeDisposable _delays = new CompositeDisposable();
                private object _gate = new object();

                private readonly Func<TSource, IObservable<TDelay>> _delaySelector;

                public _(Func<TSource, IObservable<TDelay>> delaySelector, IObserver<TSource> observer)
                    : base(observer)
                {
                    _delaySelector = delaySelector;
                }

                private bool _atEnd;
                private IDisposable _subscription;

                public void Run(TParent parent)
                {
                    _atEnd = false;

                    Disposable.SetSingle(ref _subscription, RunCore(parent));
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing)
                    {
                        Disposable.TryDispose(ref _subscription);
                        _delays.Dispose();
                    }
                    base.Dispose(disposing);
                }

                protected abstract IDisposable RunCore(TParent parent);

                public override void OnNext(TSource value)
                {
                    var delay = default(IObservable<TDelay>);
                    try
                    {
                        delay = _delaySelector(value);
                    }
                    catch (Exception error)
                    {
                        lock (_gate)
                        {
                            ForwardOnError(error);
                        }

                        return;
                    }

                    var d = new SingleAssignmentDisposable();
                    _delays.Add(d);
                    d.Disposable = delay.SubscribeSafe(new DelayObserver(this, value, d));
                }

                public override void OnError(Exception error)
                {
                    lock (_gate)
                    {
                        ForwardOnError(error);
                    }
                }

                public override void OnCompleted()
                {
                    lock (_gate)
                    {
                        _atEnd = true;
                        _subscription.Dispose();

                        CheckDone();
                    }
                }

                private void CheckDone()
                {
                    if (_atEnd && _delays.Count == 0)
                    {
                        ForwardOnCompleted();
                    }
                }

                private sealed class DelayObserver : IObserver<TDelay>
                {
                    private readonly _ _parent;
                    private readonly TSource _value;
                    private readonly IDisposable _self;

                    public DelayObserver(_ parent, TSource value, IDisposable self)
                    {
                        _parent = parent;
                        _value = value;
                        _self = self;
                    }

                    public void OnNext(TDelay value)
                    {
                        lock (_parent._gate)
                        {
                            _parent.ForwardOnNext(_value);

                            _parent._delays.Remove(_self);
                            _parent.CheckDone();
                        }
                    }

                    public void OnError(Exception error)
                    {
                        lock (_parent._gate)
                        {
                            _parent.ForwardOnError(error);
                        }
                    }

                    public void OnCompleted()
                    {
                        lock (_parent._gate)
                        {
                            _parent.ForwardOnNext(_value);

                            _parent._delays.Remove(_self);
                            _parent.CheckDone();
                        }
                    }
                }
            }
        }

        internal class Selector : Base<Selector>
        {
            private readonly Func<TSource, IObservable<TDelay>> _delaySelector;

            public Selector(IObservable<TSource> source, Func<TSource, IObservable<TDelay>> delaySelector)
                : base(source)
            {
                _delaySelector = delaySelector;
            }

            protected override Base<Selector>._ CreateSink(IObserver<TSource> observer) => new _(_delaySelector, observer);

            protected override void Run(Base<Selector>._ sink) => sink.Run(this);

            new private sealed class _ : Base<Selector>._
            {
                public _(Func<TSource, IObservable<TDelay>> delaySelector, IObserver<TSource> observer)
                    : base(delaySelector, observer)
                {
                }

                protected override IDisposable RunCore(Selector parent) => parent._source.SubscribeSafe(this);
            }
        }

        internal sealed class SelectorWithSubscriptionDelay : Base<SelectorWithSubscriptionDelay>
        {
            private readonly IObservable<TDelay> _subscriptionDelay;
            private readonly Func<TSource, IObservable<TDelay>> _delaySelector;

            public SelectorWithSubscriptionDelay(IObservable<TSource> source, IObservable<TDelay> subscriptionDelay, Func<TSource, IObservable<TDelay>> delaySelector)
                : base(source)
            {
                _subscriptionDelay = subscriptionDelay;
                _delaySelector = delaySelector;
            }

            protected override Base<SelectorWithSubscriptionDelay>._ CreateSink(IObserver<TSource> observer) => new _(_delaySelector, observer);

            protected override void Run(Base<SelectorWithSubscriptionDelay>._ sink) => sink.Run(this);

            new private sealed class _ : Base<SelectorWithSubscriptionDelay>._
            {
                public _(Func<TSource, IObservable<TDelay>> delaySelector, IObserver<TSource> observer)
                    : base(delaySelector, observer)
                {
                }

                protected override IDisposable RunCore(SelectorWithSubscriptionDelay parent)
                {
                    var delayConsumer = new SubscriptionDelayObserver(this, parent._source);

                    delayConsumer.SetFirst(parent._subscriptionDelay.SubscribeSafe(delayConsumer));

                    return delayConsumer;
                }

                private sealed class SubscriptionDelayObserver : IObserver<TDelay>, IDisposable
                {
                    private readonly _ _parent;
                    private readonly IObservable<TSource> _source;
                    private IDisposable _subscription;

                    public SubscriptionDelayObserver(_ parent, IObservable<TSource> source)
                    {
                        _parent = parent;
                        _source = source;
                    }

                    internal void SetFirst(IDisposable d)
                    {
                        Disposable.TrySetSingle(ref _subscription, d);
                    }

                    public void OnNext(TDelay value)
                    {
                        Disposable.TrySetSerial(ref _subscription, _source.SubscribeSafe(_parent));
                    }

                    public void OnError(Exception error)
                    {
                        _parent.ForwardOnError(error);
                    }

                    public void OnCompleted()
                    {
                        Disposable.TrySetSerial(ref _subscription, _source.SubscribeSafe(_parent));
                    }

                    public void Dispose()
                    {
                        Disposable.TryDispose(ref _subscription);
                    }
                }
            }
        }
    }
}
