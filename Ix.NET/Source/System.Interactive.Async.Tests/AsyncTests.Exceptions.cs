﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information. 

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using System.Threading;
using System.Threading.Tasks;

namespace Tests
{
    public partial class AsyncTests
    {
        [Fact]
        public void Catch_Null()
        {
            AssertThrows<ArgumentNullException>(() => AsyncEnumerable.Catch<int, Exception>(default(IAsyncEnumerable<int>), x => null));
            AssertThrows<ArgumentNullException>(() => AsyncEnumerable.Catch<int, Exception>(AsyncEnumerable.Return(42), default(Func<Exception, IAsyncEnumerable<int>>)));

            AssertThrows<ArgumentNullException>(() => AsyncEnumerable.Catch<int>(default(IAsyncEnumerable<int>), AsyncEnumerable.Return(42)));
            AssertThrows<ArgumentNullException>(() => AsyncEnumerable.Catch<int>(AsyncEnumerable.Return(42), default(IAsyncEnumerable<int>)));
            AssertThrows<ArgumentNullException>(() => AsyncEnumerable.Catch<int>(default(IAsyncEnumerable<int>[])));
            AssertThrows<ArgumentNullException>(() => AsyncEnumerable.Catch<int>(default(IEnumerable<IAsyncEnumerable<int>>)));
        }

        [Fact]
        public void Catch1()
        {
            var err = false;
            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable();
            var ys = new[] { 4, 5, 6 }.ToAsyncEnumerable();

            var res = xs.Catch<int, Exception>(ex_ => { err = true; return ys; });

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);
            NoNext(e);

            Assert.False(err);
        }

        [Fact]
        public void Catch2()
        {
            var ex = new InvalidOperationException("Bang!");

            var err = false;
            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable().Concat(AsyncEnumerable.Throw<int>(ex));
            var ys = new[] { 4, 5, 6 }.ToAsyncEnumerable();

            var res = xs.Catch<int, InvalidOperationException>(ex_ => { err = true; return ys; });

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);

            Assert.False(err);

            HasNext(e, 4);

            Assert.True(err);

            HasNext(e, 5);
            HasNext(e, 6);
            NoNext(e);
        }

        [Fact]
        public void Catch3()
        {
            var ex = new InvalidOperationException("Bang!");

            var err = false;
            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable().Concat(AsyncEnumerable.Throw<int>(ex));
            var ys = new[] { 4, 5, 6 }.ToAsyncEnumerable();

            var res = xs.Catch<int, Exception>(ex_ => { err = true; return ys; });

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);

            Assert.False(err);

            HasNext(e, 4);

            Assert.True(err);

            HasNext(e, 5);
            HasNext(e, 6);
            NoNext(e);
        }

        [Fact]
        public void Catch4()
        {
            var ex = new DivideByZeroException();

            var err = false;
            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable().Concat(AsyncEnumerable.Throw<int>(ex));
            var ys = new[] { 4, 5, 6 }.ToAsyncEnumerable();

            var res = xs.Catch<int, InvalidOperationException>(ex_ => { err = true; return ys; });

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);

            AssertThrows<Exception>(() => e.MoveNext().Wait(WaitTimeoutMs), ex_ => ((AggregateException)ex_).Flatten().InnerExceptions.Single() == ex);

            Assert.False(err);
        }

        [Fact]
        public void Catch5()
        {
            var ex = new InvalidOperationException("Bang!");
            var ex2 = new Exception("Oops!");

            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable().Concat(AsyncEnumerable.Throw<int>(ex));
            var ys = new[] { 4, 5, 6 }.ToAsyncEnumerable();

            var res = xs.Catch<int, InvalidOperationException>(ex_ => { if (ex_.Message == "Bang!") throw ex2; return ys; });

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);

            AssertThrows<Exception>(() => e.MoveNext().Wait(WaitTimeoutMs), ex_ => ((AggregateException)ex_).Flatten().InnerExceptions.Single() == ex2);
        }

        [Fact]
        public void Catch6()
        {
            var ex = new InvalidOperationException("Bang!");

            var err = false;
            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable().Concat(AsyncEnumerable.Throw<int>(ex));

            var res = xs.Catch<int, InvalidOperationException>(ex_ => { err = true; return xs; });

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);

            Assert.False(err);

            HasNext(e, 1);

            Assert.True(err);

            HasNext(e, 2);
            HasNext(e, 3);

            AssertThrows<Exception>(() => e.MoveNext().Wait(WaitTimeoutMs), ex_ => ((AggregateException)ex_).Flatten().InnerExceptions.Single() == ex);
        }

        [Fact]
        public void Catch7()
        {
            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable();
            var ys = new[] { 4, 5, 6 }.ToAsyncEnumerable();

            var res = AsyncEnumerable.Catch(xs, ys);

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);
            NoNext(e);
        }

        [Fact]
        public void Catch8()
        {
            var ex = new InvalidOperationException("Bang!");

            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable().Concat(AsyncEnumerable.Throw<int>(ex));
            var ys = new[] { 4, 5, 6 }.ToAsyncEnumerable();

            var res = AsyncEnumerable.Catch(xs, ys);

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);
            HasNext(e, 4);
            HasNext(e, 5);
            HasNext(e, 6);
            NoNext(e);
        }

        [Fact]
        public void Catch9()
        {
            var ex = new InvalidOperationException("Bang!");

            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable().Concat(AsyncEnumerable.Throw<int>(ex));
            var ys = new[] { 4, 5, 6 }.ToAsyncEnumerable();

            var res = AsyncEnumerable.Catch(new[] { xs, xs, ys, ys });

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);
            HasNext(e, 4);
            HasNext(e, 5);
            HasNext(e, 6);
            NoNext(e);
        }

        [Fact]
        public void Catch10()
        {
            var res = CatchXss().Catch();

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);

            AssertThrows<Exception>(() => e.MoveNext().Wait(WaitTimeoutMs), ex_ => ((AggregateException)ex_).Flatten().InnerExceptions.Single().Message == "Bang!");
        }

        private IEnumerable<IAsyncEnumerable<int>> CatchXss()
        {
            yield return new[] { 1, 2, 3 }.ToAsyncEnumerable().Concat(AsyncEnumerable.Throw<int>(new Exception("!!!")));
            throw new Exception("Bang!");
        }

        [Fact]
        public void Catch11()
        {
            var ex = new InvalidOperationException("Bang!");

            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable().Concat(AsyncEnumerable.Throw<int>(ex));

            var res = AsyncEnumerable.Catch(new[] { xs, xs });

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);

            AssertThrows<Exception>(() => e.MoveNext().Wait(WaitTimeoutMs), ex_ => ((AggregateException)ex_).Flatten().InnerExceptions.Single() == ex);
        }

        [Fact]
        public void Catch12()
        {
            var res = AsyncEnumerable.Catch(Enumerable.Empty<IAsyncEnumerable<int>>());

            var e = res.GetEnumerator();
            NoNext(e);
        }

        [Fact]
        public async Task Catch13()
        {
            var ex = new InvalidOperationException("Bang!");

            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable().Concat(AsyncEnumerable.Throw<int>(ex));
            var ys = new[] { 4, 5, 6 }.ToAsyncEnumerable();

            var res = AsyncEnumerable.Catch(new[] { xs, xs, ys, ys });

            await SequenceIdentity(res);
        }

        [Fact]
        public async Task Catch14()
        {
            var err = false;
            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable();
            var ys = new[] { 4, 5, 6 }.ToAsyncEnumerable();

            var res = xs.Catch<int, Exception>(ex_ => { err = true; return ys; });

            await SequenceIdentity(res);

            Assert.False(err);
        }

        [Fact]
        public void Finally_Null()
        {
            AssertThrows<ArgumentNullException>(() => AsyncEnumerable.Finally(default(IAsyncEnumerable<int>), () => { }));
            AssertThrows<ArgumentNullException>(() => AsyncEnumerable.Finally(AsyncEnumerable.Return(42), null));
        }

        [Fact]
        public void Finally1()
        {
            var b = false;

            var xs = AsyncEnumerable.Empty<int>().Finally(() => { b = true; });

            var e = xs.GetEnumerator();

            Assert.False(b);
            NoNext(e);

            Assert.True(b);
        }

        [Fact]
        public void Finally2()
        {
            var b = false;

            var xs = AsyncEnumerable.Return(42).Finally(() => { b = true; });

            var e = xs.GetEnumerator();

            Assert.False(b);
            HasNext(e, 42);

            Assert.False(b);
            NoNext(e);

            Assert.True(b);
        }

        [Fact]
        public void Finally3()
        {
            var ex = new Exception("Bang!");

            var b = false;

            var xs = AsyncEnumerable.Throw<int>(ex).Finally(() => { b = true; });

            var e = xs.GetEnumerator();

            Assert.False(b);
            AssertThrows<Exception>(() => e.MoveNext().Wait(WaitTimeoutMs), ex_ => ((AggregateException)ex_).Flatten().InnerExceptions.Single() == ex);

            Assert.True(b);
        }

        [Fact]
        public void Finally4()
        {
            var b = false;

            var xs = new[] { 1, 2 }.ToAsyncEnumerable().Finally(() => { b = true; });

            var e = xs.GetEnumerator();

            Assert.False(b);
            HasNext(e, 1);

            Assert.False(b);
            HasNext(e, 2);

            Assert.False(b);
            NoNext(e);

            Assert.True(b);
        }

        [Fact]
        public void Finally5()
        {
            var b = false;

            var xs = new[] { 1, 2 }.ToAsyncEnumerable().Finally(() => { b = true; });

            var e = xs.GetEnumerator();

            Assert.False(b);
            HasNext(e, 1);

            e.Dispose();

            Assert.True(b);
        }

        [Fact]
        public async Task Finally6()
        {
            var b = false;

            var xs = new[] { 1, 2 }.ToAsyncEnumerable().Finally(() => { Volatile.Write(ref b, true); });

            var e = xs.GetEnumerator();

            var cts = new CancellationTokenSource();

            var t = e.MoveNext(cts.Token);
            cts.Cancel();
            t.Wait(WaitTimeoutMs);

            for (var i = 0; i < WaitTimeoutMs / 100; i++)
            {
                if (Volatile.Read(ref b))
                {
                    return;
                }
                await Task.Delay(100);
            }

            Assert.True(true, "Timeout while waiting for b to become true.");
        }

        [Fact]
        public async Task Finally7()
        {
            var i = 0;
            var xs = new[] { 1, 2 }.ToAsyncEnumerable().Finally(() => { i++; });

            await SequenceIdentity(xs);
            Assert.Equal(2, i);
        }
    

    [Fact]
        public void OnErrorResumeNext_Null()
        {
            AssertThrows<ArgumentNullException>(() => AsyncEnumerable.OnErrorResumeNext<int>(default(IAsyncEnumerable<int>), AsyncEnumerable.Return(42)));
            AssertThrows<ArgumentNullException>(() => AsyncEnumerable.OnErrorResumeNext<int>(AsyncEnumerable.Return(42), default(IAsyncEnumerable<int>)));
            AssertThrows<ArgumentNullException>(() => AsyncEnumerable.OnErrorResumeNext<int>(default(IAsyncEnumerable<int>[])));
            AssertThrows<ArgumentNullException>(() => AsyncEnumerable.OnErrorResumeNext<int>(default(IEnumerable<IAsyncEnumerable<int>>)));
        }

        [Fact]
        public void OnErrorResumeNext7()
        {
            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable();
            var ys = new[] { 4, 5, 6 }.ToAsyncEnumerable();

            var res = AsyncEnumerable.OnErrorResumeNext(xs, ys);

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);
            HasNext(e, 4);
            HasNext(e, 5);
            HasNext(e, 6);
            NoNext(e);
        }

        [Fact]
        public void OnErrorResumeNext8()
        {
            var ex = new InvalidOperationException("Bang!");

            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable().Concat(AsyncEnumerable.Throw<int>(ex));
            var ys = new[] { 4, 5, 6 }.ToAsyncEnumerable();

            var res = AsyncEnumerable.OnErrorResumeNext(xs, ys);

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);
            HasNext(e, 4);
            HasNext(e, 5);
            HasNext(e, 6);
            NoNext(e);
        }

        [Fact]
        public void OnErrorResumeNext9()
        {
            var ex = new InvalidOperationException("Bang!");

            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable().Concat(AsyncEnumerable.Throw<int>(ex));
            var ys = new[] { 4, 5, 6 }.ToAsyncEnumerable();

            var res = AsyncEnumerable.OnErrorResumeNext(new[] { xs, xs, ys, ys });

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);
            HasNext(e, 4);
            HasNext(e, 5);
            HasNext(e, 6);
            HasNext(e, 4);
            HasNext(e, 5);
            HasNext(e, 6);
            NoNext(e);
        }

        [Fact]
        public void OnErrorResumeNext10()
        {
            var res = OnErrorResumeNextXss().OnErrorResumeNext();

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);

            AssertThrows<Exception>(() => e.MoveNext().Wait(WaitTimeoutMs), ex_ => ((AggregateException)ex_).Flatten().InnerExceptions.Single().Message == "Bang!");
        }

        private IEnumerable<IAsyncEnumerable<int>> OnErrorResumeNextXss()
        {
            yield return new[] { 1, 2, 3 }.ToAsyncEnumerable().Concat(AsyncEnumerable.Throw<int>(new Exception("!!!")));
            throw new Exception("Bang!");
        }

        [Fact]
        public void OnErrorResumeNext11()
        {
            var ex = new InvalidOperationException("Bang!");

            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable().Concat(AsyncEnumerable.Throw<int>(ex));

            var res = AsyncEnumerable.OnErrorResumeNext(new[] { xs, xs });

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);
            NoNext(e);
        }

        [Fact]
        public void OnErrorResumeNext12()
        {
            var res = AsyncEnumerable.OnErrorResumeNext(Enumerable.Empty<IAsyncEnumerable<int>>());

            var e = res.GetEnumerator();
            NoNext(e);
        }

        [Fact]
        public async Task OnErrorResumeNext13()
        {
            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable();
            var ys = new[] { 4, 5, 6 }.ToAsyncEnumerable();

            var res = AsyncEnumerable.OnErrorResumeNext(xs, ys);

            await SequenceIdentity(res);
        }

        [Fact]
        public void Retry_Null()
        {
            AssertThrows<ArgumentNullException>(() => AsyncEnumerable.Retry<int>(default(IAsyncEnumerable<int>)));

            AssertThrows<ArgumentNullException>(() => AsyncEnumerable.Retry<int>(default(IAsyncEnumerable<int>), 1));
            AssertThrows<ArgumentOutOfRangeException>(() => AsyncEnumerable.Retry<int>(AsyncEnumerable.Return(42), -1));
        }

        [Fact]
        public void Retry1()
        {
            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable();

            var res = xs.Retry();

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);
            NoNext(e);
        }

        [Fact]
        public void Retry2()
        {
            var ex = new InvalidOperationException("Bang!");

            var xs = new[] { 1, 2, 3 }.ToAsyncEnumerable().Concat(AsyncEnumerable.Throw<int>(ex));

            var res = xs.Retry();

            var e = res.GetEnumerator();
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);
            HasNext(e, 1);
            HasNext(e, 2);
            HasNext(e, 3);
        }
    }
}
