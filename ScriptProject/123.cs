using Microsoft.AspNetCore.Identity;

public class User
{
    public string Username { get; set; }
}

var user = new User { Username = "gleb" };
var passwordHasher = new PasswordHasher<User>();
// Replace "your_password_here" with whatever password you want to use!
string hashedPassword = passwordHasher.HashPassword(user, "your_password_here");

Console.WriteLine($"Admin__Username=gleb");
Console.WriteLine($"Admin__PasswordHash={hashedPassword}");
