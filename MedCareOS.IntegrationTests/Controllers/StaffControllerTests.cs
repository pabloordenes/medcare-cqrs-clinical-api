using System.Net;
using FluentAssertions;
using MedCareOS.Domain.Entities;
using MedCareOS.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace MedCareOS.IntegrationTests.Controllers;

public class StaffControllerTests : IClassFixture<CustomApiFactory>
{
    private readonly HttpClient _client;
    private readonly CustomApiFactory _factory;
    
    public StaffControllerTests(CustomApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task StaffEndpoints_ShouldNotHaveRouteCollision_BetweenGetAllAndLookup()
    {
        // arrange
        var requestAllStaff = "/api/staff";
        var requestLookup = "api/staff/lookup?specialty=Cardiologia";
        
        // act
        var responseAllStaff = await _client.GetAsync(requestAllStaff);
        var responseLookup = await _client.GetAsync(requestLookup);
        
        // assert
        responseAllStaff.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        responseAllStaff.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        
        responseLookup.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        responseLookup.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
    
    [Fact]
    public async Task GetStaffLookup_WhenAuthenticated_ShouldReturn200Ok()
    {
        // arrange
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            
            // inyectamos un registro falso con los datos exactos que espera el Handler
            var testStaff = Staff.Create(
                userId: Guid.NewGuid(),
                firstName: "Test",
                lastName: "Médico",
                rut: "11111111-1",
                specialty: "Cardiología", // el filtro que usaremos en la URL
                roleName: "Médico Cardiólogo",
                phone: "+56900000000"
            );
    
            // usamos db.Set<T>() para no depender del nombre del DbSet original
            db.Set<Staff>().Add(testStaff);
            await db.SaveChangesAsync();
        }
        
        var specialtyTarget = "Cardiología"; 
        var request = $"/api/staff/lookup?specialty={Uri.EscapeDataString(specialtyTarget)}";
        
        // inyectamos pasaporte jwt falso
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");

        // act
        var response = await _client.GetAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // assert
        response.StatusCode.Should().Be(
            HttpStatusCode.OK, 
            because: $"la respuesta de la API falló con detalle: {content}"
        );
    }
}