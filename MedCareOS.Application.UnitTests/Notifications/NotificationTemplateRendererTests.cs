using System.Linq.Expressions;
using FluentAssertions;
using MedCareOS.Application.Common.Exceptions;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Interfaces;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Application.Notifications.Templates;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace MedCareOS.Application.UnitTests.Notifications;

public class NotificationTemplateRendererTests
{
    [Theory]
    [InlineData(NotificationType.AppointmentCreated)]
    [InlineData(NotificationType.Reminder)]
    [InlineData(NotificationType.FinalConfirmation)]
    [InlineData(NotificationType.OverbookNotice)]
    [InlineData(NotificationType.WaitingListOffer)]
    [InlineData(NotificationType.OverbookOfferCancelled)]
    [InlineData(NotificationType.RescheduleHelp)]
    public async Task RenderAsync_AllCatalogTemplates_ShouldPreviewCleanlyInEsClWithoutUnresolvedPlaceholders(
        NotificationType type)
    {
        var renderer = CreateRenderer([]);
        var templateKey = DefaultNotificationTemplateCatalog.GetTemplateKey(type);
        var appointmentId = Guid.NewGuid();

        var baseParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["patientFirstName"] = "Camila",
            ["clinicName"] = "Clínica San José",
            ["appointmentDate"] = "25/11/2026",
            ["appointmentTime"] = "10:30 hrs",
            ["specialtyDisplay"] = "Cardiología",
            ["deadline"] = "24/11/2026 20:00 hrs"
        };

        var actionUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["confirmUrl"] = "https://app.medcare.cl/appointments/respond?token=secret_tok_123&action=confirm",
            ["cancelUrl"] = "https://app.medcare.cl/appointments/respond?token=secret_tok_123&action=cancel",
            ["rescheduleUrl"] = "https://app.medcare.cl/appointments/reschedule?token=secret_tok_123"
        };

        var waRendered = await renderer.RenderAsync(new TemplateRenderRequest(
            RiskThresholds.DefaultTenantId,
            appointmentId,
            type,
            NotificationChannel.WhatsApp,
            templateKey,
            "es-CL",
            baseParams,
            actionUrls,
            "?token=secret_tok_123"));

        var emailRendered = await renderer.RenderAsync(new TemplateRenderRequest(
            RiskThresholds.DefaultTenantId,
            appointmentId,
            type,
            NotificationChannel.Email,
            templateKey,
            "es-CL",
            baseParams,
            actionUrls,
            "?token=secret_tok_123"));

        waRendered.PlainTextBody.Should().NotBeNullOrWhiteSpace();
        waRendered.PlainTextBody.Should().NotContain("{{");
        waRendered.MetaTemplateName.Should().NotBeNullOrWhiteSpace();
        waRendered.MetaOrderedBodyParameters.Should().NotBeEmpty();

        emailRendered.Subject.Should().NotBeNullOrWhiteSpace();
        emailRendered.PlainTextBody.Should().NotContain("{{");
        emailRendered.HtmlBody.Should().Contain("<!DOCTYPE html>");
        emailRendered.SanitizedContentForStorage.Should().NotContain("secret_tok_123");
    }

    [Fact]
    public async Task RenderAsync_FinalConfirmation_ContainsRequiredOverbookWarningCopy()
    {
        var renderer = CreateRenderer([]);
        var rendered = await renderer.RenderAsync(new TemplateRenderRequest(
            RiskThresholds.DefaultTenantId,
            Guid.NewGuid(),
            NotificationType.FinalConfirmation,
            NotificationChannel.WhatsApp,
            DefaultNotificationTemplateCatalog.Keys.FinalConfirmation,
            "es-CL",
            new Dictionary<string, string>
            {
                ["patientFirstName"] = "Juan",
                ["clinicName"] = "Clínica MedCare",
                ["appointmentDate"] = "26/11/2026",
                ["appointmentTime"] = "09:00 hrs",
                ["deadline"] = "25/11/2026 18:00 hrs"
            }));

        rendered.PlainTextBody.Should().Contain("podríamos asignar tu horario a otro paciente (sobrecupo)");
        rendered.PlainTextBody.Should().Contain("se atenderá primero a quien haya confirmado antes");
        rendered.Buttons.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("symptoms")]
    [InlineData("sintomas")]
    [InlineData("nlpSummary")]
    [InlineData("diagnosis")]
    [InlineData("rut")]
    public async Task RenderAsync_WhenClinicalParameterProvided_ThrowsClinicalPrivacyViolationException(string forbiddenKey)
    {
        var renderer = CreateRenderer([]);

        Func<Task> act = () => renderer.RenderAsync(new TemplateRenderRequest(
            RiskThresholds.DefaultTenantId,
            Guid.NewGuid(),
            NotificationType.AppointmentCreated,
            NotificationChannel.Email,
            DefaultNotificationTemplateCatalog.Keys.AppointmentCreated,
            "es-CL",
            new Dictionary<string, string>
            {
                ["patientFirstName"] = "Ana",
                ["clinicName"] = "Clínica MedCare",
                ["appointmentDate"] = "26/11/2026",
                ["appointmentTime"] = "09:00 hrs",
                ["specialtyDisplay"] = "Medicina General",
                [forbiddenKey] = "Dolor torácico"
            }));

        await act.Should().ThrowAsync<ClinicalPrivacyViolationException>();
    }

    [Fact]
    public async Task RenderAsync_WhenTenantOverrideExists_UsesTenantCustomTemplate()
    {
        var tenantId = Guid.NewGuid();
        var customTemplate = NotificationTemplate.Create(
            tenantId,
            DefaultNotificationTemplateCatalog.Keys.AppointmentCreated,
            NotificationChannel.Email,
            "es-CL",
            version: 2,
            subjectTemplate: "[VIP] Cita confirmada en {{clinicName}}",
            bodyTemplate: "Estimado/a {{patientFirstName}}, te esperamos en {{clinicName}} el {{appointmentDate}} a las {{appointmentTime}} ({{specialtyDisplay}}).");

        var renderer = CreateRenderer([customTemplate]);

        var rendered = await renderer.RenderAsync(new TemplateRenderRequest(
            tenantId,
            Guid.NewGuid(),
            NotificationType.AppointmentCreated,
            NotificationChannel.Email,
            DefaultNotificationTemplateCatalog.Keys.AppointmentCreated,
            "es-CL",
            new Dictionary<string, string>
            {
                ["patientFirstName"] = "Pedro",
                ["clinicName"] = "Clínica Norte",
                ["appointmentDate"] = "27/11/2026",
                ["appointmentTime"] = "11:15 hrs",
                ["specialtyDisplay"] = "Oftalmología"
            }));

        rendered.Subject.Should().Be("[VIP] Cita confirmada en Clínica Norte");
        rendered.PlainTextBody.Should().StartWith("Estimado/a Pedro, te esperamos");
    }

    [Fact]
    public void FormattingHelper_FormatsUtcToChileLocalTime()
    {
        // 2026-11-25 14:30 UTC in Santiago (-03:00 summer) = 11:30 hrs
        var utc = new DateTime(2026, 11, 25, 14, 30, 0, DateTimeKind.Utc);
        var (dateLocal, timeLocal) = NotificationFormattingHelper.FormatAppointmentDateTime(utc, "America/Santiago");

        dateLocal.Should().Be("25-11-2026");
        timeLocal.Should().Be("11:30 hrs");
    }

    private static NotificationTemplateRenderer CreateRenderer(List<NotificationTemplate> templates)
    {
        var mockDbSet = CreateAsyncDbSetMock(templates);
        var dbContext = new Mock<IApplicationDbContext>();
        dbContext.Setup(x => x.NotificationTemplates).Returns(mockDbSet.Object);
        return new NotificationTemplateRenderer(dbContext.Object);
    }

    private static Mock<DbSet<T>> CreateAsyncDbSetMock<T>(List<T> data) where T : class
    {
        var queryable = data.AsQueryable();
        var mockSet = new Mock<DbSet<T>>();
        mockSet.As<IAsyncEnumerable<T>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(data.GetEnumerator()));
        mockSet.As<IQueryable<T>>()
            .Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        mockSet.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mockSet.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mockSet.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(() => queryable.GetEnumerator());
        return mockSet;
    }

    private sealed class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
    {
        private readonly IQueryProvider _inner;

        public TestAsyncQueryProvider(IQueryProvider inner) => _inner = inner;

        public IQueryable CreateQuery(Expression expression) => new TestAsyncEnumerable<TEntity>(expression);
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new TestAsyncEnumerable<TElement>(expression);
        public object? Execute(Expression expression) => _inner.Execute(expression);
        public TResult Execute<TResult>(Expression expression) => _inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            var expectedResultType = typeof(TResult).GetGenericArguments()[0];
            var executionResult = typeof(IQueryProvider)
                .GetMethod(nameof(IQueryProvider.Execute), 1, [typeof(Expression)])!
                .MakeGenericMethod(expectedResultType)
                .Invoke(this, [expression]);

            return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(expectedResultType)
                .Invoke(null, [executionResult])!;
        }
    }

    private sealed class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public TestAsyncEnumerable(Expression expression) : base(expression) { }
        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
    }

    private sealed class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;
        public TestAsyncEnumerator(IEnumerator<T> inner) => _inner = inner;
        public T Current => _inner.Current;
        public ValueTask DisposeAsync() { _inner.Dispose(); return ValueTask.CompletedTask; }
        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(_inner.MoveNext());
    }
}
