using System.Collections.Generic;
using ExpenseHub.Api.Admin;
using ExpenseHub.Api.Common;
using ExpenseHub.Api.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Admin;

/// <summary>
/// Testa as regras de alteração de roles sem banco ou servidor HTTP.
/// </summary>
[TestClass]
public sealed class RoleChangePolicyTests
{
    /// <summary>Uma role arbitrária não pode ser atribuída.</summary>
    [TestMethod]
    public void UnknownRoleIsRejected()
    {
        ServiceResult<IReadOnlyList<string>> result = RoleChangePolicy.Evaluate("target", "actor", ["Owner"]);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ServiceErrorKind.Validation, result.Error!.Kind);
    }

    /// <summary>Nomes válidos são normalizados para o nome canônico.</summary>
    [TestMethod]
    public void RoleNamesAreCaseInsensitiveAndCanonical()
    {
        ServiceResult<IReadOnlyList<string>> result = RoleChangePolicy.Evaluate("target", "actor", ["employee", "APPROVER"]);

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(new[] { AppRoles.Employee, AppRoles.Approver }, new List<string>(result.Value));
    }

    /// <summary>O administrador não pode remover sua própria permissão.</summary>
    [TestMethod]
    public void ActorCannotRemoveOwnAdminRole()
    {
        ServiceResult<IReadOnlyList<string>> result = RoleChangePolicy.Evaluate("actor", "actor", [AppRoles.Employee]);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ServiceErrorKind.Validation, result.Error!.Kind);
    }

    /// <summary>A role Admin de outro usuário pode ser removida.</summary>
    [TestMethod]
    public void ActorCanRemoveAnotherUsersAdminRole()
    {
        ServiceResult<IReadOnlyList<string>> result = RoleChangePolicy.Evaluate("target", "actor", []);

        Assert.IsTrue(result.Succeeded);
        Assert.IsEmpty(result.Value);
    }

    /// <summary>Roles repetidas não geram associações duplicadas.</summary>
    [TestMethod]
    public void DuplicateRolesAreCollapsedAfterNormalization()
    {
        ServiceResult<IReadOnlyList<string>> result = RoleChangePolicy.Evaluate(
            "target", "actor", [AppRoles.Employee, "EMPLOYEE", "employee"]);

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(new[] { AppRoles.Employee }, new List<string>(result.Value));
    }

    /// <summary>Uma lista ausente é diferente de remover todas as roles.</summary>
    [TestMethod]
    public void NullRoleListIsRejected()
    {
        ServiceResult<IReadOnlyList<string>> result = RoleChangePolicy.Evaluate("target", "actor", null);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ServiceErrorKind.Validation, result.Error!.Kind);
    }

    /// <summary>Manter Admin permite que o ator altere suas demais roles.</summary>
    [TestMethod]
    public void ActorCanRetainOwnAdminWithDifferentCasing()
    {
        ServiceResult<IReadOnlyList<string>> result = RoleChangePolicy.Evaluate("actor", "actor", ["ADMIN"]);

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(new[] { AppRoles.Admin }, new List<string>(result.Value));
    }

    /// <summary>Um elemento nulo também é rejeitado como role desconhecida.</summary>
    [TestMethod]
    public void NullRoleElementIsRejected()
    {
        ServiceResult<IReadOnlyList<string>> result = RoleChangePolicy.Evaluate("target", "actor", [null!]);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ServiceErrorKind.Validation, result.Error!.Kind);
    }
}
