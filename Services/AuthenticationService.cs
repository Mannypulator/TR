using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using PetProjectOne.Models;
using PetProjectOne.Entities;
using PetProjectOne.Db;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace PetProjectOne.Services;

public class AuthenticationService : IAuthenticationService
{
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public AuthenticationService (UserManager<User> userManager, IConfiguration configuration, RoleManager<IdentityRole> roleManager, AppDbContext dbContext)
    {
        _userManager = userManager;
        _configuration = configuration;
        _roleManager = roleManager;
        _dbContext = dbContext;
    }

    // Register User
    public async Task<string> Register(RegisterRequest request)
    {
        var userByEmail = await _userManager.FindByEmailAsync(request.Email);
        var userByUsername = await _userManager.FindByNameAsync(request.UserName);
        if (userByEmail is not null || userByUsername is not null)
        {
            throw new ArgumentException($"User with email {request.Email} or username {request.UserName} already exists.");
        }

        User user = new()
        {
            FullName = request.FullName,
            Email = request.Email,
            UserName = request.UserName,
            SecurityStamp = Guid.NewGuid().ToString(),
            IsTasker = false
        };

        var result = await _userManager.CreateAsync(user, request.Password);

        if(!result.Succeeded)
        {
            throw new ArgumentException($"Unable to register user {request.UserName} errors: {GetErrorsText(result.Errors)}");
        }

        return await Login(new LoginRequest { Username = request.Email, Password = request.Password });
    }

    public async Task<string> RegisterTasker(TaskerRegisterRequest request)
    {
        var user = new User
        {
            UserName = request.UserName,
            Email = request.Email,
            FullName = request.FullName,
            IsTasker = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
            return string.Join(", ", result.Errors.Select(e => e.Description));

        // Assign "Tasker" role
        await _userManager.AddToRoleAsync(user, "MemberTasker");

        // Create TaskerProfile
        var taskerProfile = new TaskerProfile
        {
            UserId = user.Id,
            Skills = request.Skills,
            ExperienceLevel = request.ExperienceLevel,
            HourlyRate = request.HourlyRate,
            SelectedCategory = request.SelectedCategory,
            CategoryId = request.CategoryId
        };
        _dbContext.TaskerProfiles.Add(taskerProfile);
        await _dbContext.SaveChangesAsync();

        return "Tasker registered successfully!";
    }


    // login user
    public async Task<string> Login(LoginRequest request)
    {
        var user = await _userManager.FindByNameAsync(request.Username);

        if(user is null)
        {
            user = await _userManager.FindByEmailAsync(request.Username);
        }

        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            throw new ArgumentException($"Unable to authenticate user {request.Username}");
        }

        var authClaims = new List<Claim>
        {
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = GetToken(authClaims);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // autorization
    private JwtSecurityToken GetToken(IEnumerable<Claim> authClaims)
    {
        var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["JWT:Secret"]));

        var token = new JwtSecurityToken(
            issuer: _configuration["JWT:ValidIssuer"],
            audience: _configuration["JWT:ValidAudience"],
            expires: DateTime.Now.AddHours(3),
            claims: authClaims,
            signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256));

        return token;
    }

    private string GetErrorsText(IEnumerable<IdentityError> errors)
    {
        return string.Join(", ", errors.Select(error => error.Description).ToArray());
    }


}