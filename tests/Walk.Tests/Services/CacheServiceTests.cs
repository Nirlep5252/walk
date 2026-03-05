using System.IO;
using FluentAssertions;
using Walk.Services;

namespace Walk.Tests.Services;

public class CacheServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly CacheService _cache;

    public CacheServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _cache = new CacheService(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task GetOrSetAsync_Returns_Fresh_Data_On_First_Call()
    {
        var result = await _cache.GetOrSetAsync("test.json", TimeSpan.FromHours(6),
            () => Task.FromResult(new TestData { Value = 42 }));

        result.Should().NotBeNull();
        result!.Value.Should().Be(42);
    }

    [Fact]
    public async Task GetOrSetAsync_Returns_Cached_Data_On_Second_Call()
    {
        int callCount = 0;
        Task<TestData> Factory() => Task.FromResult(new TestData { Value = ++callCount });

        await _cache.GetOrSetAsync("test.json", TimeSpan.FromHours(6), Factory);
        var result = await _cache.GetOrSetAsync("test.json", TimeSpan.FromHours(6), Factory);

        result!.Value.Should().Be(1);
    }

    [Fact]
    public async Task GetOrSetAsync_Refreshes_When_TTL_Expired()
    {
        int callCount = 0;
        Task<TestData> Factory() => Task.FromResult(new TestData { Value = ++callCount });

        await _cache.GetOrSetAsync("test.json", TimeSpan.Zero, Factory);
        var result = await _cache.GetOrSetAsync("test.json", TimeSpan.Zero, Factory);

        result!.Value.Should().Be(2);
    }

    private class TestData
    {
        public int Value { get; set; }
    }
}
