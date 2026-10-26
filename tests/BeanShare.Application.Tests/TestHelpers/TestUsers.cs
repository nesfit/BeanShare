using BeanShare.Application.Abstractions;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using NSubstitute;

namespace BeanShare.Application.Tests.TestHelpers;

/// <summary>User repositories for handler tests: the uncached access and merge lookups (DS-30).</summary>
public static class TestUsers
{
    /// <summary>Every id is an existing, active, real user; nobody is merged.</summary>
    public static IUserRepository ActiveRepository()
    {
        var repository = Substitute.For<IUserRepository>();
        repository.GetAccessStatusAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(new UserAccessStatus(true, true, false, null));
        repository.GetMergedMapAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<UserId, UserId>());
        return repository;
    }

    /// <summary>A repository answering from <paramref name="users"/>, read at call time.</summary>
    public static IUserRepository RepositoryOver(IDictionary<UserId, User> users)
    {
        var repository = Substitute.For<IUserRepository>();
        repository.GetAccessStatusAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(ci => users.TryGetValue(ci.Arg<UserId>(), out var user)
                ? new UserAccessStatus(true, user.IsActive, user.IsPlaceholder, user.MergedIntoUserId)
                : UserAccessStatus.Missing);
        repository.GetMergedMapAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyDictionary<UserId, UserId>)ci.Arg<IReadOnlyCollection<UserId>>()
                .Where(id => users.TryGetValue(id, out var user) && user.MergedIntoUserId.HasValue)
                .Distinct()
                .ToDictionary(id => id, id => users[id].MergedIntoUserId!.Value));
        repository.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => users.Values.FirstOrDefault(u =>
                string.Equals(u.Email, ci.Arg<string>().Trim(), StringComparison.OrdinalIgnoreCase)));
        repository.GetByIdAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(ci => users.TryGetValue(ci.Arg<UserId>(), out var user) ? user : null);
        repository.AddAsync(Arg.Do<User>(u => users[u.Id] = u), Arg.Any<CancellationToken>());
        return repository;
    }
}
