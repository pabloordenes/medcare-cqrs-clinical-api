using MedCareOS.Domain.Common;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class PatientRepository : IPatientRepository
{
    private readonly ApplicationDbContext _dbContext;

    public PatientRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Patient?> GetPatientByIdAsync(Guid patientId, CancellationToken cancellationToken)
    {
        return await _dbContext.Patients.FirstOrDefaultAsync(x => x.Id == patientId, cancellationToken);
    }

    public async Task<bool> ExistsByRutAsync(string rut, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Patients.AnyAsync(x => x.Rut == rut, cancellationToken);
    }

    public void Add(Patient patient)
    {
        _dbContext.Patients.Add(patient);
    }

    public async Task<Patient?> GetByUserId(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Patients.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
    }

    public async Task<Patient?> GetByNormalizedPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        var targetDigits = PhoneNormalizer.ToWhatsAppDigits(phone);
        if (string.IsNullOrWhiteSpace(targetDigits))
            return null;

        var e164 = $"+{targetDigits}";
        var rawTrimmed = phone.Trim();

        var exact = await _dbContext.Patients.FirstOrDefaultAsync(
            p => p.Phone == e164 || p.Phone == targetDigits || p.Phone == rawTrimmed,
            cancellationToken);

        if (exact is not null)
            return exact;

        var suffix = targetDigits.Length >= 4 ? targetDigits[^4..] : targetDigits;
        var candidates = await _dbContext.Patients
            .Where(p => p.Phone.Contains(suffix))
            .ToListAsync(cancellationToken);

        return candidates.FirstOrDefault(
            p => string.Equals(PhoneNormalizer.ToWhatsAppDigits(p.Phone), targetDigits, StringComparison.Ordinal));
    }
}
