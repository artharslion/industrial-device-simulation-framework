using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.State;
using IndustrialSim.Scenarios;

namespace IndustrialSim.Scenarios.Tests;

public class ScenarioConditionTests
{
    private static StateStore Store() => new(new DeviceDefinition(new DeviceId("pump-001"), "pump", new[]
    {
        new DataPointDefinition("temperature", DataType.Double, DataPointAccess.Read, 95d),
        new DataPointDefinition("running", DataType.Boolean, DataPointAccess.Read, true)
    }));

    [Theory]
    [InlineData("temperature > 90", true)]
    [InlineData("temperature < 90", false)]
    [InlineData("temperature == 95", true)]
    [InlineData("running == true", true)]
    [InlineData("running == false", false)]
    public void Evaluates_supported_scalar_conditions_from_state_store(string expression, bool expected) => Assert.Equal(expected, ConditionEvaluator.Evaluate(expression, Store()));

    [Fact]
    public void Rejects_unsupported_expression_syntax() => Assert.Throws<ArgumentException>(() => ConditionEvaluator.Evaluate("temperature >= 90", Store()));
}
