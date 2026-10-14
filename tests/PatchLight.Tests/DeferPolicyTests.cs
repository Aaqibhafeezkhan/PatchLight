using PatchLight.Core;
using PatchLight.Windows;

namespace PatchLight.Tests;

public class DeferPolicyTests
{
    private sealed class FakeValues : IPolicyValues
    {
        public Dictionary<string, int> Values { get; } = [];
        public int? Get(string name) => Values.TryGetValue(name, out var v) ? v : null;
        public void Set(string name, int value) => Values[name] = value;
        public void Delete(string name) => Values.Remove(name);
    }

    [Fact]
    public void Turning_the_wait_on_writes_windows_policy()
    {
        var values = new FakeValues();
        Assert.True(DeferPolicy.Apply(values, 7, owns: false));
        Assert.Equal(1, values.Get(DeferPolicy.Enabled));
        Assert.Equal(7, values.Get(DeferPolicy.Period));
    }

    [Fact]
    public void Changing_the_wait_updates_its_own_policy()
    {
        var values = new FakeValues();
        DeferPolicy.Apply(values, 7, owns: false);
        Assert.True(DeferPolicy.Apply(values, 14, owns: true));
        Assert.Equal(14, values.Get(DeferPolicy.Period));
    }

    [Fact]
    public void Turning_the_wait_off_removes_only_its_own_policy()
    {
        var values = new FakeValues();
        DeferPolicy.Apply(values, 7, owns: false);
        Assert.False(DeferPolicy.Apply(values, 0, owns: true));
        Assert.Empty(values.Values);
    }

    [Fact]
    public void Someone_elses_policy_is_never_touched()
    {
        var values = new FakeValues();
        values.Set(DeferPolicy.Enabled, 1);
        values.Set(DeferPolicy.Period, 10);

        Assert.True(DeferPolicy.IsManagedElsewhere(values, owns: false));
        Assert.False(DeferPolicy.Apply(values, 0, owns: false));
        var ex = Assert.Throws<PatchLightException>(() => DeferPolicy.Apply(values, 7, owns: false));
        Assert.Contains("organization", ex.Message);
        Assert.Equal(10, values.Get(DeferPolicy.Period));
        Assert.Equal(1, values.Get(DeferPolicy.Enabled));
    }

    [Fact]
    public void Nothing_set_is_not_managed_elsewhere()
    {
        Assert.False(DeferPolicy.IsManagedElsewhere(new FakeValues(), owns: false));
        var values = new FakeValues();
        DeferPolicy.Apply(values, 3, owns: false);
        Assert.False(DeferPolicy.IsManagedElsewhere(values, owns: true));
    }
}
