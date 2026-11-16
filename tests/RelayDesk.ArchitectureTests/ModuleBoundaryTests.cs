using System.Reflection;
using NetArchTest.Rules;
using RelayDesk.Modules.Audit;
using RelayDesk.Modules.Tenancy;
using RelayDesk.SharedKernel.Modules;

namespace RelayDesk.ArchitectureTests;

/// <summary>Enforces ADR 0002 module rules. Add each new module assembly to <see cref="Modules"/>.</summary>
public class ModuleBoundaryTests
{
    private static readonly Assembly[] Modules = [typeof(TenancyModule).Assembly, typeof(AuditModule).Assembly];

    public static TheoryData<string> ModuleNames() => new(Modules.Select(m => m.GetName().Name!));

    private static Assembly Get(string name) => Modules.Single(m => m.GetName().Name == name);

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_does_not_depend_on_other_modules(string name)
    {
        var others = Modules.Where(m => m.GetName().Name != name)
            .Select(m => m.GetName().Name!).ToArray();
        if (others.Length == 0) return;

        var result = Types.InAssembly(Get(name)).ShouldNot().HaveDependencyOnAny(others).GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_does_not_depend_on_hosts(string name)
    {
        var result = Types.InAssembly(Get(name)).ShouldNot()
            .HaveDependencyOnAny("RelayDesk.Api", "RelayDesk.Realtime", "RelayDesk.AiWorker")
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Only_contracts_and_module_entry_are_public(string name)
    {
        var result = Types.InAssembly(Get(name))
            .That().ArePublic()
            .And().DoNotResideInNamespaceEndingWith(".Contracts")
            .And().DoNotResideInNamespaceEndingWith(".Migrations") // EF generates these as public
            .And().DoNotImplementInterface(typeof(IModule))
            .Should().NotBePublic()
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    private static string Failing(TestResult r) =>
        "Violations: " + string.Join(", ", r.FailingTypeNames ?? []);
}
