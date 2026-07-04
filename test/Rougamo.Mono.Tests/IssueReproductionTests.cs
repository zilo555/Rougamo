using System.Collections.Generic;
using System.Threading.Tasks;
using Rougamo.Fody.Tests;
using Xunit;

namespace Rougamo.Mono.Tests;

public class IssueReproductionTests
{
    [Fact]
    public void Test_Mono_Runtime_Does_Not_Crash()
    {
        var weavedAssembly = new WeavedAssembly("Issues");
        var instance = weavedAssembly.GetInstance("Issue108`1", false, t => t.MakeGenericType(typeof(string)));
        var result = instance.CatchAndGetStackTrace(108, "mono-stacktrace");
        Assert.True(true);
    }

    [Fact]
    public async Task Test_Mono_Generic_Class_NonGeneric_Async_Iterator_Methods()
    {
        var weavedAssembly = new WeavedAssembly("MonoTest");
        var instance = weavedAssembly.GetInstance("MonoCase`1", false, t => t.MakeGenericType(typeof(string)));
        var logs = new List<string>();

        // async method in generic class
        var asyncResult = await instance.AsyncMethod(logs);
        Assert.Equal(42, asyncResult);
        Assert.Equal(["AsyncMethod OnEntry", "AsyncMethod OnSuccess", "AsyncMethod OnExit"], logs);
        logs.Clear();

        // iterator method in generic class
        var enumerator = (IEnumerator<int>)instance.IteratorMethod(logs);
        while (enumerator.MoveNext()) { }
        Assert.Equal(["IteratorMethod OnEntry", "IteratorMethod OnSuccess", "IteratorMethod OnExit"], logs);
        logs.Clear();

        // async iterator method in generic class
        var asyncEnumerator = (IAsyncEnumerator<int>)instance.AsyncIteratorMethod(logs);
        while (await asyncEnumerator.MoveNextAsync()) { }
        Assert.Equal(["AsyncIteratorMethod OnEntry", "AsyncIteratorMethod OnSuccess", "AsyncIteratorMethod OnExit"], logs);
    }
}
