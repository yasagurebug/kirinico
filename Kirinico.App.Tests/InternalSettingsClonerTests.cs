using Kirinico.App.Services;
using Kirinico.Core.Models;

namespace Kirinico.App.Tests;

public sealed class InternalSettingsClonerTests
{
    [Fact]
    public void Clone_CreatesDeepCopy()
    {
        var source = new InternalSettings();
        source.Matting.Cf.MaxIters = 123;

        var clone = InternalSettingsCloner.Clone(source);
        source.Matting.Cf.MaxIters = 999;

        Assert.Equal(123, clone.Matting.Cf.MaxIters);
    }

    [Fact]
    public void CopyTo_CopiesNestedValues()
    {
        var source = new InternalSettings();
        source.BackgroundThreshold.TbgMax = 42d;
        var target = new InternalSettings();

        InternalSettingsCloner.CopyTo(target, source);

        Assert.Equal(42d, target.BackgroundThreshold.TbgMax);
    }
}
