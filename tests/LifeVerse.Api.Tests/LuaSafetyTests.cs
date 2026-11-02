using LifeVerse.Api.Modules.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class LuaSafetyTests
{
    private static LuaRuntimeOptions Options => new() { MaxSourceBytes = 1024, MaxInstructions = 20 };

    [Fact]
    public void Validator_RejectsDangerousApisAndInfiniteLoops()
    {
        Assert.False(LuaSafetyValidator.Validate("while true do end", Options).Valid);
        Assert.False(LuaSafetyValidator.Validate("return os.execute('whoami')", Options).Valid);
        Assert.False(LuaSafetyValidator.Validate(new string('x', 2000), Options).Valid);
    }

    [Fact]
    public void Validator_AllowsTypedEventsOnly()
    {
        Assert.True(LuaSafetyValidator.IsEvent("on_touch"));
        Assert.False(LuaSafetyValidator.IsEvent("on_sql"));
        Assert.True(LuaSafetyValidator.Validate("function on_touch(event)\n object.send_message('ok')\nend", Options).Valid);
    }
}
