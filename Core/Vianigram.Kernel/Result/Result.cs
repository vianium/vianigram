// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Kernel.Result
{
    /// <summary>
    /// Result&lt;T, TError&gt; — success or error without exceptions crossing boundaries.
    /// Struct to avoid allocations on hot paths.
    ///
    /// USAGE:
    ///     var r = Result&lt;int, Error&gt;.Ok(42);
    ///     var e = Result&lt;int, Error&gt;.Fail(new Error("bad", "input"));
    ///     if (r.IsOk) use(r.Value); else log(r.Error);
    /// </summary>
    public struct Result<T, TError>
    {
        private readonly T _value;
        private readonly TError _error;
        private readonly bool _isOk;

        private Result(T value, TError error, bool isOk)
        {
            _value = value;
            _error = error;
            _isOk = isOk;
        }

        public static Result<T, TError> Ok(T value)
        {
            return new Result<T, TError>(value, default(TError), true);
        }

        public static Result<T, TError> Fail(TError error)
        {
            return new Result<T, TError>(default(T), error, false);
        }

        public bool IsOk { get { return _isOk; } }
        public bool IsFail { get { return !_isOk; } }

        public T Value
        {
            get
            {
                if (!_isOk) throw new InvalidOperationException("Result is in failed state. Check IsOk first.");
                return _value;
            }
        }

        public TError Error
        {
            get
            {
                if (_isOk) throw new InvalidOperationException("Result is in ok state. Check IsFail first.");
                return _error;
            }
        }

        public T ValueOrDefault(T fallback)
        {
            return _isOk ? _value : fallback;
        }

        public TR Match<TR>(Func<T, TR> ok, Func<TError, TR> fail)
        {
            if (ok == null) throw new ArgumentNullException("ok");
            if (fail == null) throw new ArgumentNullException("fail");
            return _isOk ? ok(_value) : fail(_error);
        }

        public Result<U, TError> Map<U>(Func<T, U> mapper)
        {
            if (mapper == null) throw new ArgumentNullException("mapper");
            return _isOk ? Result<U, TError>.Ok(mapper(_value)) : Result<U, TError>.Fail(_error);
        }

        public Result<U, TError> Bind<U>(Func<T, Result<U, TError>> binder)
        {
            if (binder == null) throw new ArgumentNullException("binder");
            return _isOk ? binder(_value) : Result<U, TError>.Fail(_error);
        }

        public Result<T, TError> Tap(Action<T> sideEffect)
        {
            if (_isOk && sideEffect != null) sideEffect(_value);
            return this;
        }
    }

    /// <summary>
    /// Static helpers for type inference.
    /// </summary>
    public static class Result
    {
        public static Result<T, TError> Ok<T, TError>(T value)
        {
            return Result<T, TError>.Ok(value);
        }

        public static Result<T, TError> Fail<T, TError>(TError error)
        {
            return Result<T, TError>.Fail(error);
        }

        public static Result<T, Error> Try<T>(Func<T> operation, string errorCode = null)
        {
            try
            {
                return Result<T, Error>.Ok(operation());
            }
            catch (Exception ex)
            {
                return Result<T, Error>.Fail(Error.From(ex, errorCode));
            }
        }
    }
}
