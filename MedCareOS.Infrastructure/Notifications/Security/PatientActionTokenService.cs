using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Interfaces;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedCareOS.Infrastructure.Notifications.Security;

public class PatientActionTokenService : IPatientActionTokenService
{
    private const int MinSecretLength = 32;

    private readonly IPatientActionTokenRepository _tokenRepository;
    private readonly IApplicationDbContext _dbContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly NotificationOptions _options;

    public PatientActionTokenService(
        IPatientActionTokenRepository tokenRepository,
        IApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        IOptions<NotificationOptions> options)
    {
        _tokenRepository = tokenRepository;
        _dbContext = dbContext;
        _unitOfWork = unitOfWork;
        _options = options.Value;
    }

    public async Task<IssuedActionToken> IssueAsync(
        Guid appointmentId,
        Guid patientId,
        Guid? notificationId,
        PatientActionScope scope,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "El TTL del token debe ser mayor a cero.");

        var utcNow = DateTimeOffset.UtcNow;
        if (notificationId.HasValue)
        {
            await _tokenRepository.RevokeActiveForNotificationAsync(notificationId.Value, utcNow, cancellationToken);
        }

        var tokenId = Guid.NewGuid();
        var tokenIdBytes = tokenId.ToByteArray();
        var hmacBytes = ComputeHmac(tokenIdBytes);
        var rawToken = $"{Base64Url.EncodeToString(tokenIdBytes)}.{Base64Url.EncodeToString(hmacBytes)}";
        var tokenHash = ComputeSha256Hex(rawToken);
        var expiresAt = utcNow.Add(ttl);

        var entity = PatientActionToken.Create(
            tokenId,
            tokenHash,
            appointmentId,
            patientId,
            notificationId,
            scope,
            expiresAt,
            utcNow);

        await _tokenRepository.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new IssuedActionToken(tokenId, rawToken, expiresAt);
    }

    public async Task<IssuedActionToken> IssueForWaitingListOfferAsync(
        Guid waitingListOfferId,
        Guid patientId,
        Guid? notificationId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "El TTL del token debe ser mayor a cero.");

        var utcNow = DateTimeOffset.UtcNow;
        await _tokenRepository.RevokeActiveForWaitingListOfferAsync(waitingListOfferId, utcNow, cancellationToken);

        var tokenId = Guid.NewGuid();
        var tokenIdBytes = tokenId.ToByteArray();
        var hmacBytes = ComputeHmac(tokenIdBytes);
        var rawToken = $"{Base64Url.EncodeToString(tokenIdBytes)}.{Base64Url.EncodeToString(hmacBytes)}";
        var tokenHash = ComputeSha256Hex(rawToken);
        var expiresAt = utcNow.Add(ttl);

        var entity = PatientActionToken.CreateForWaitingListOffer(
            tokenId,
            tokenHash,
            waitingListOfferId,
            patientId,
            notificationId,
            expiresAt,
            utcNow);

        await _tokenRepository.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new IssuedActionToken(tokenId, rawToken, expiresAt);
    }

    public async Task<ActionTokenPreviewResult> PreviewAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        if (!TryVerifyRawToken(rawToken, out var tokenId, out var tokenHash))
            return new ActionTokenPreviewResult(false, ActionTokenValidationStatus.InvalidSignatureOrFormat);

        var token = await _dbContext.PatientActionTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.Id == tokenId, cancellationToken);

        var utcNow = DateTimeOffset.UtcNow;
        var invalidStatus = ValidateTokenState(token, utcNow);
        if (invalidStatus.HasValue)
            return new ActionTokenPreviewResult(false, invalidStatus.Value);

        if (token!.WaitingListOfferId.HasValue)
        {
            var offerSummary = await (
                from offer in _dbContext.WaitingListOffers.AsNoTracking()
                join block in _dbContext.ScheduleBlocks.AsNoTracking() on offer.ScheduleBlockId equals block.Id
                join staff in _dbContext.Staffs.AsNoTracking() on block.StaffId equals staff.Id
                where offer.Id == token.WaitingListOfferId.Value && offer.PatientId == token.PatientId
                select new
                {
                    offer.Id,
                    offer.Status,
                    offer.ExpiresAt,
                    block.StartTime,
                    block.IsOverbook,
                    block.OverbookStatus,
                    staff.Specialty
                }
            ).FirstOrDefaultAsync(cancellationToken);

            var utcNowDate = utcNow.UtcDateTime;
            if (offerSummary is null ||
                offerSummary.Status != WaitingListOfferStatus.Pending ||
                offerSummary.ExpiresAt <= utcNow ||
                offerSummary.StartTime <= utcNowDate ||
                (offerSummary.IsOverbook && offerSummary.OverbookStatus is not (OverbookBlockStatus.Open or OverbookBlockStatus.Offered)))
            {
                return new ActionTokenPreviewResult(false, ActionTokenValidationStatus.AppointmentNotActionable);
            }

            var (offerDateLocal, offerTimeLocal) = NotificationFormattingHelper.FormatAppointmentDateTime(
                offerSummary.StartTime,
                _options.TimeZoneId);

            var offerSpecialtyDisplay = _options.IncludeSpecialtyInMessages
                ? NotificationFormattingHelper.ResolveSpecialtyDisplay(
                    offerSummary.Specialty,
                    _options.IncludeSpecialtyInMessages,
                    _options.SensitiveSpecialtiesExcluded)
                : null;

            return new ActionTokenPreviewResult(
                IsValid: true,
                ValidationStatus: ActionTokenValidationStatus.Valid,
                ClinicName: _options.ClinicName,
                AppointmentDateLocal: offerDateLocal,
                AppointmentTimeLocal: offerTimeLocal,
                Specialty: offerSpecialtyDisplay,
                AllowedActions: [PatientReplyIntent.Confirm, PatientReplyIntent.Cancel, PatientReplyIntent.OptOut],
                ExpiresAt: offerSummary.ExpiresAt < token.ExpiresAt ? offerSummary.ExpiresAt : token.ExpiresAt,
                WaitingListOfferId: offerSummary.Id,
                TargetKind: "WaitingListOffer");
        }

        var summary = await (
            from app in _dbContext.Appointments.AsNoTracking()
            join block in _dbContext.ScheduleBlocks.AsNoTracking() on app.ScheduleBlockId equals block.Id
            join staff in _dbContext.Staffs.AsNoTracking() on block.StaffId equals staff.Id
            where app.Id == token.AppointmentId && app.PatientId == token.PatientId
            select new
            {
                app.Status,
                block.StartTime,
                staff.Specialty
            }
        ).FirstOrDefaultAsync(cancellationToken);

        if (summary is null ||
            summary.Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow or AppointmentStatus.Finished or AppointmentStatus.InProgress)
        {
            return new ActionTokenPreviewResult(false, ActionTokenValidationStatus.AppointmentNotActionable);
        }

        var (dateLocal, timeLocal) = NotificationFormattingHelper.FormatAppointmentDateTime(
            summary.StartTime,
            _options.TimeZoneId);

        var specialtyDisplay = _options.IncludeSpecialtyInMessages
            ? NotificationFormattingHelper.ResolveSpecialtyDisplay(
                summary.Specialty,
                _options.IncludeSpecialtyInMessages,
                _options.SensitiveSpecialtiesExcluded)
            : null;

        IReadOnlyList<PatientReplyIntent> allowedActions = token.ActionScope switch
        {
            PatientActionScope.ConfirmOrCancel => [PatientReplyIntent.Confirm, PatientReplyIntent.Cancel, PatientReplyIntent.OptOut],
            PatientActionScope.Reschedule => [PatientReplyIntent.OptOut],
            PatientActionScope.OptOut => [PatientReplyIntent.OptOut],
            _ => []
        };

        return new ActionTokenPreviewResult(
            IsValid: true,
            ValidationStatus: ActionTokenValidationStatus.Valid,
            ClinicName: _options.ClinicName,
            AppointmentDateLocal: dateLocal,
            AppointmentTimeLocal: timeLocal,
            Specialty: specialtyDisplay,
            AllowedActions: allowedActions,
            ExpiresAt: token.ExpiresAt,
            WaitingListOfferId: null,
            TargetKind: "Appointment");
    }

    public async Task<ActionTokenConsumeResult> ConsumeAsync(
        string rawToken,
        PatientReplyIntent requestedAction,
        CancellationToken cancellationToken = default)
    {
        if (!TryVerifyRawToken(rawToken, out var tokenId, out var tokenHash))
            return new ActionTokenConsumeResult(false, ActionTokenValidationStatus.InvalidSignatureOrFormat);

        var token = await _tokenRepository.GetByHashAsync(tokenHash, cancellationToken);
        if (token is null || token.Id != tokenId)
            return new ActionTokenConsumeResult(false, ActionTokenValidationStatus.NotFound);

        var utcNow = DateTimeOffset.UtcNow;
        var invalidStatus = ValidateTokenState(token, utcNow);
        if (invalidStatus.HasValue)
            return new ActionTokenConsumeResult(false, invalidStatus.Value);

        if (!ScopeAllows(token.ActionScope, requestedAction))
            return new ActionTokenConsumeResult(false, ActionTokenValidationStatus.ScopeNotAllowed);

        if (requestedAction is PatientReplyIntent.Confirm or PatientReplyIntent.Cancel)
        {
            if (token.WaitingListOfferId.HasValue)
            {
                var offerState = await (
                    from offer in _dbContext.WaitingListOffers.AsNoTracking()
                    join block in _dbContext.ScheduleBlocks.AsNoTracking() on offer.ScheduleBlockId equals block.Id
                    where offer.Id == token.WaitingListOfferId.Value && offer.PatientId == token.PatientId
                    select new
                    {
                        offer.Status,
                        offer.ExpiresAt,
                        block.StartTime,
                        block.IsOverbook,
                        block.OverbookStatus
                    }
                ).FirstOrDefaultAsync(cancellationToken);

                if (offerState is null ||
                    offerState.Status != WaitingListOfferStatus.Pending ||
                    offerState.ExpiresAt <= utcNow ||
                    offerState.StartTime <= utcNow.UtcDateTime ||
                    (offerState.IsOverbook && offerState.OverbookStatus is not (OverbookBlockStatus.Open or OverbookBlockStatus.Offered)))
                {
                    return new ActionTokenConsumeResult(false, ActionTokenValidationStatus.AppointmentNotActionable);
                }
            }
            else
            {
                var appointment = await _dbContext.Appointments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == token.AppointmentId && a.PatientId == token.PatientId, cancellationToken);

                if (appointment is null ||
                    appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow or AppointmentStatus.Finished or AppointmentStatus.InProgress)
                {
                    return new ActionTokenConsumeResult(false, ActionTokenValidationStatus.AppointmentNotActionable);
                }
            }
        }

        token.MarkUsed(requestedAction.ToString(), utcNow);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ActionTokenConsumeResult(false, ActionTokenValidationStatus.AlreadyUsed);
        }

        return new ActionTokenConsumeResult(
            IsSuccess: true,
            ValidationStatus: ActionTokenValidationStatus.Valid,
            AppointmentId: token.AppointmentId,
            PatientId: token.PatientId,
            NotificationId: token.NotificationId,
            ExecutedIntent: requestedAction,
            WaitingListOfferId: token.WaitingListOfferId);
    }

    public async Task<ActionTokenConsumeResult> ValidateRescheduleTokenAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        if (!TryVerifyRawToken(rawToken, out var tokenId, out var tokenHash))
            return new ActionTokenConsumeResult(false, ActionTokenValidationStatus.InvalidSignatureOrFormat);

        var token = await _dbContext.PatientActionTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.Id == tokenId, cancellationToken);

        var utcNow = DateTimeOffset.UtcNow;
        var invalidStatus = ValidateTokenState(token, utcNow);
        if (invalidStatus.HasValue)
            return new ActionTokenConsumeResult(false, invalidStatus.Value);

        if (token!.ActionScope != PatientActionScope.Reschedule || !token.AppointmentId.HasValue)
            return new ActionTokenConsumeResult(false, ActionTokenValidationStatus.ScopeNotAllowed);

        var appointment = await _dbContext.Appointments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == token.AppointmentId.Value && a.PatientId == token.PatientId, cancellationToken);

        if (appointment is null ||
            appointment.Status is not (AppointmentStatus.Scheduled or AppointmentStatus.Confirmed))
        {
            return new ActionTokenConsumeResult(false, ActionTokenValidationStatus.AppointmentNotActionable);
        }

        return new ActionTokenConsumeResult(
            IsSuccess: true,
            ValidationStatus: ActionTokenValidationStatus.Valid,
            AppointmentId: token.AppointmentId,
            PatientId: token.PatientId,
            NotificationId: token.NotificationId,
            ExecutedIntent: PatientReplyIntent.Unknown);
    }

    public async Task<ActionTokenConsumeResult> ConsumeRescheduleTokenAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        if (!TryVerifyRawToken(rawToken, out var tokenId, out var tokenHash))
            return new ActionTokenConsumeResult(false, ActionTokenValidationStatus.InvalidSignatureOrFormat);

        var token = await _tokenRepository.GetByHashAsync(tokenHash, cancellationToken);
        if (token is null || token.Id != tokenId)
            return new ActionTokenConsumeResult(false, ActionTokenValidationStatus.NotFound);

        var utcNow = DateTimeOffset.UtcNow;
        var invalidStatus = ValidateTokenState(token, utcNow);
        if (invalidStatus.HasValue)
            return new ActionTokenConsumeResult(false, invalidStatus.Value);

        if (token.ActionScope != PatientActionScope.Reschedule || !token.AppointmentId.HasValue)
            return new ActionTokenConsumeResult(false, ActionTokenValidationStatus.ScopeNotAllowed);

        var appointment = await _dbContext.Appointments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == token.AppointmentId.Value && a.PatientId == token.PatientId, cancellationToken);

        if (appointment is null ||
            appointment.Status is not (AppointmentStatus.Scheduled or AppointmentStatus.Confirmed))
        {
            return new ActionTokenConsumeResult(false, ActionTokenValidationStatus.AppointmentNotActionable);
        }

        token.MarkUsed("reschedule", utcNow);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ActionTokenConsumeResult(false, ActionTokenValidationStatus.AlreadyUsed);
        }

        return new ActionTokenConsumeResult(
            IsSuccess: true,
            ValidationStatus: ActionTokenValidationStatus.Valid,
            AppointmentId: token.AppointmentId,
            PatientId: token.PatientId,
            NotificationId: token.NotificationId,
            ExecutedIntent: PatientReplyIntent.Unknown);
    }

    private bool TryVerifyRawToken(string rawToken, out Guid tokenId, out string tokenHash)
    {
        tokenId = Guid.Empty;
        tokenHash = string.Empty;

        if (string.IsNullOrWhiteSpace(rawToken))
            return false;

        var parts = rawToken.Trim().Split('.', 3);
        if (parts.Length != 2)
            return false;

        try
        {
            var tokenIdBytes = Base64Url.DecodeFromChars(parts[0]);
            var providedHmac = Base64Url.DecodeFromChars(parts[1]);

            if (tokenIdBytes.Length != 16 || providedHmac.Length != 32)
                return false;

            var expectedHmac = ComputeHmac(tokenIdBytes);
            if (!CryptographicOperations.FixedTimeEquals(providedHmac, expectedHmac))
                return false;

            tokenId = new Guid(tokenIdBytes);
            tokenHash = ComputeSha256Hex(rawToken.Trim());
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private byte[] ComputeHmac(byte[] data)
    {
        var secret = _options.ActionTokenSecret;
        if (string.IsNullOrWhiteSpace(secret) || secret.Trim().Length < MinSecretLength)
        {
            throw new InvalidOperationException(
                $"Notifications:ActionTokenSecret debe configurarse con al menos {MinSecretLength} caracteres.");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret.Trim()));
        return hmac.ComputeHash(data);
    }

    private static string ComputeSha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static ActionTokenValidationStatus? ValidateTokenState(PatientActionToken? token, DateTimeOffset utcNow)
    {
        if (token is null)
            return ActionTokenValidationStatus.NotFound;
        if (token.UsedAt.HasValue)
            return ActionTokenValidationStatus.AlreadyUsed;
        if (token.RevokedAt.HasValue)
            return ActionTokenValidationStatus.Revoked;
        if (token.ExpiresAt <= utcNow)
            return ActionTokenValidationStatus.Expired;
        return null;
    }

    private static bool ScopeAllows(PatientActionScope scope, PatientReplyIntent intent) => intent switch
    {
        PatientReplyIntent.Confirm or PatientReplyIntent.Cancel
            => scope is PatientActionScope.ConfirmOrCancel or PatientActionScope.WaitingListOffer,
        PatientReplyIntent.OptOut => true,
        _ => false
    };
}
