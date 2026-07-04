using Rougamo;
using Rougamo.Context;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MonoTest;

public class MonoMoAttribute : MoAttribute
{
    public override void OnEntry(MethodContext context)
    {
        var logs = (List<string>)context.Arguments[0];
        logs.Add($"{context.Method.Name} OnEntry");
    }

    public override void OnSuccess(MethodContext context)
    {
        var logs = (List<string>)context.Arguments[0];
        logs.Add($"{context.Method.Name} OnSuccess");
    }

    public override void OnExit(MethodContext context)
    {
        var logs = (List<string>)context.Arguments[0];
        logs.Add($"{context.Method.Name} OnExit");
    }
}

public class MonoCase<T>
{
    [MonoMo]
    public string CatchAndGetStackTrace(int code = 108, string message = "mono-stacktrace")
    {
        try
        {
            ThrowCore(code, message);
            return string.Empty;
        }
        catch (Exception ex)
        {
            return ex.StackTrace ?? string.Empty;
        }
    }

    private void ThrowCore(int code, string message)
    {
        throw new InvalidOperationException($"{typeof(T).Name}:{code}:{message}");
    }

    // 泛型类型中的非泛型 async 方法，验证 Mono JIT 对状态机 method token 的处理
    [MonoMo]
    public async Task<int> AsyncMethod(List<string> logs)
    {
        await Task.Yield();
        return 42;
    }

    // 泛型类型中的非泛型 iterator 方法
    [MonoMo]
    public IEnumerator<int> IteratorMethod(List<string> logs)
    {
        yield return 1;
        yield return 2;
        yield return 3;
    }

    // 泛型类型中的非泛型 async iterator 方法
    [MonoMo]
    public async IAsyncEnumerator<int> AsyncIteratorMethod(List<string> logs)
    {
        await Task.Yield();
        yield return 1;
        yield return 2;
        yield return 3;
    }
}
