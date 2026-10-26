using BeanShare.Application.Abstractions;
using BeanShare.Infrastructure.Communication.Templates;

namespace BeanShare.Infrastructure.Communication.Services;

/// <summary>
/// Implementation of settlement email template service.
/// </summary>
public sealed class SettlementEmailTemplateService : ISettlementEmailTemplateService
{
    /// <inheritdoc />
    public (string HtmlBody, string PlainTextBody) GenerateSettlementEmail(SettlementEmailModel model, string? appUrl = null)
    {
        var htmlBody = SettlementEmailTemplate.GenerateHtml(model, appUrl);
        var plainTextBody = SettlementEmailTemplate.GeneratePlainText(model);

        return (htmlBody, plainTextBody);
    }
}
