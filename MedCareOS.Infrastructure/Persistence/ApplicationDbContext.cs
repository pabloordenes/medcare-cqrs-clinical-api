using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
    
    public DbSet<Appointment> Appointments { get; set; }
    public DbSet<Patient> Patients { get; set; }
    public DbSet<ScheduleBlock> ScheduleBlocks { get; set; }
    public DbSet<Box> Boxes { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<Staff> Staffs { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<WaitingList> WaitingLists { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
            
        base.OnModelCreating(builder);
    }
}