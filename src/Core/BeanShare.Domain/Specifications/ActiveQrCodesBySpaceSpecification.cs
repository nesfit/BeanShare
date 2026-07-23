using System.Linq.Expressions;
using BeanShare.Domain.Entities;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Domain.Specifications;

public sealed class ActiveQrCodesBySpaceSpecification : Spec<ActiveQrCode>
{
    private readonly SpaceId _spaceId;

    public ActiveQrCodesBySpaceSpecification(SpaceId spaceId)
    {
        _spaceId = spaceId;
    }

    public override Expression<Func<ActiveQrCode, bool>> Criteria => qr => qr.SpaceId == _spaceId && qr.IsActive;
    public override string? Reason => $"Active QR codes for space {_spaceId}";
}
