using MealPlanner.Api.Data;
using MealPlanner.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=mealplanner.db"));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    AppDbContext.Initialize(scope.ServiceProvider.GetRequiredService<AppDbContext>());
}

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapFoodEndpoints();
app.MapRecipeEndpoints();
app.MapMenuEndpoints();

app.Run();
