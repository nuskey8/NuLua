namespace NuLua;

public static class LuaAsyncStateExtensions
{
    public static LuaFunction CreateFunction<TState>(
        this ILuaState<TState> state,
        AsyncLuaFunc<TState> function,
        int upvalueCount = 0
    )
        where TState : ILuaState<TState>
    {
        state.NewFunction(function, upvalueCount);
        return new LuaFunction(state, state.Ref());
    }

    public static void RegisterFunction<TState>(
        this ILuaState<TState> state,
        ReadOnlySpan<char> name,
        AsyncLuaFunc<TState> function,
        int upvalueCount = 0
    )
        where TState : ILuaState<TState>
    {
        state.NewFunction(function, upvalueCount);
        state.SetGlobal(name);
    }

    public static ValueTask CallAsync(
        this ILuaState state,
        int argCount,
        int resultCount,
        CancellationToken cancellationToken = default
    )
    {
        var co = state.CreateThread();
        state.Pop(1);
        state.XMove(co, argCount + 1);
        return RunAndPushAsync(state, co, argCount, resultCount, cancellationToken);
    }

    public static ValueTask<LuaValue[]> ResumeAsync(
        this ILuaState state,
        LuaValue[] args,
        CancellationToken cancellationToken = default
    )
    {
        return ResumeAsync(state, args.AsMemory(), cancellationToken);
    }

    public static async ValueTask<LuaValue[]> ResumeAsync(
        this ILuaState state,
        ReadOnlyMemory<LuaValue> args,
        CancellationToken cancellationToken = default
    )
    {
        var span = args.Span;
        for (int i = 0; i < span.Length; i++)
        {
            state.Push(span[i]);
        }
        await state.ResumeAsync(args.Length, cancellationToken).ConfigureAwait(false);

        var resultCount = state.GetTop();
        var results = new LuaValue[resultCount];
        for (int i = 0; i < resultCount; i++)
        {
            results[i] = state.ToLuaValue(i + 1);
        }
        state.SetTop(0);
        return results;
    }

    public static ValueTask<LuaValue[]> DoStringAsync(
        this ILuaState state,
        ReadOnlySpan<char> code,
        ReadOnlyMemory<LuaValue> args = default,
        CancellationToken cancellationToken = default
    )
    {
        var co = state.CreateThread();
        state.Pop(1);
        co.LoadString(code, "chunk");
        var span = args.Span;
        for (int i = 0; i < span.Length; i++)
        {
            co.Push(span[i]);
        }
        return RunAndCollectAsync(state, co, args.Length, cancellationToken);
    }

    static async ValueTask RunAndPushAsync(
        ILuaState state,
        ILuaState co,
        int argCount,
        int resultCount,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await co.ResumeAsync(argCount, cancellationToken).ConfigureAwait(false);
            int actual = co.GetTop();
            if (resultCount < 0)
            {
                if (actual > 0)
                    co.XMove(state, actual);
            }
            else if (actual >= resultCount)
            {
                co.SetTop(resultCount);
                if (resultCount > 0)
                    co.XMove(state, resultCount);
            }
            else
            {
                if (actual > 0)
                    co.XMove(state, actual);
                for (int i = 0; i < resultCount - actual; i++)
                {
                    state.PushNil();
                }
            }
        }
        finally
        {
            co.Dispose();
        }
    }

    static async ValueTask<LuaValue[]> RunAndCollectAsync(
        ILuaState state,
        ILuaState co,
        int initialArgCount,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await co.ResumeAsync(initialArgCount, cancellationToken).ConfigureAwait(false);
            int top = co.GetTop();
            if (top == 0)
            {
                return [];
            }
            var baseTop = state.GetTop();
            co.XMove(state, top);
            try
            {
                var results = new LuaValue[top];
                for (int i = 0; i < top; i++)
                {
                    results[i] = state.ToLuaValue(baseTop + i + 1);
                }
                return results;
            }
            finally
            {
                state.SetTop(baseTop);
            }
        }
        finally
        {
            co.Dispose();
        }
    }
}
