using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<ScheduleBlock> ScheduleBlocks { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<Patient> Patients { get; }
    DbSet<Staff> Staffs { get; }
    DbSet<Box> Boxes { get; }
    DbSet<User> Users { get; }
    
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}