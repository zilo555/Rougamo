using BasicUsage.Attributes;
using Rougamo;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BasicUsage;

[SkipRefStruct]
public class TypeForwardedTypes
{
    public delegate string ReadOnlySpanInDelegate(List<string> logs, ReadOnlySpan<char> span);

    [TypeForwardedTypes]
    public async ValueTask ValueTaskAsync(List<string> logs)
    {
        logs.Add("ValueTaskAsync");
    }

    [TypeForwardedTypes]
    public async IAsyncEnumerable<int> AsyncEnumerable(List<string> logs)
    {
        logs.Add("AsyncEnumerable");
        yield return 1;
    }

    [TypeForwardedTypes]
    public string ReadOnlySpanIn(List<string> logs, ReadOnlySpan<char> span)
    {
        return span.ToString();
    }
}
