using AtlasSoftPlc.Domain.Ir;
using Xunit;

namespace AtlasSoftPlc.Domain.Tests.Ir;

public class PlantModelTests
{
    [Fact]
    public void PlantComponent_ShouldStoreBasicProperties()
    {
        var varId = Guid.NewGuid();
        var component = new PlantComponent
        {
            Name = "Bomba de Agua",
            VariableId = varId,
            Type = PlantComponentType.Pump
        };

        Assert.Equal("Bomba de Agua", component.Name);
        Assert.Equal(varId, component.VariableId);
        Assert.Equal(PlantComponentType.Pump, component.Type);
    }

    [Fact]
    public void PlantModel_ShouldContainMultipleComponents()
    {
        var model = new PlantModel
        {
            Components = new List<PlantComponent>
            {
                new() { Name = "Motor 1", Type = PlantComponentType.Motor },
                new() { Name = "Valvula 1", Type = PlantComponentType.Valve }
            }
        };

        Assert.Equal(2, model.Components.Count);
    }

    [Fact]
    public void PlantComponent_ShouldHandleRequirements()
    {
        var reqId = Guid.NewGuid();
        var varId = Guid.NewGuid();
        var component = new PlantComponent
        {
            Name = "Motor",
            Requires = new List<Guid> { reqId },
            RequirementBindings = new Dictionary<Guid, Guid> { { reqId, varId } }
        };

        Assert.Contains(reqId, component.Requires);
        Assert.Equal(varId, component.RequirementBindings[reqId]);
    }
}
