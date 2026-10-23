using HackBack.Domain;
using Microsoft.EntityFrameworkCore;

namespace HackBack.Infrastructure;

public class UserRepository(AppDbContext db)
{
    public Task<User?> GetByLoginAsync(string login) =>
        db.Users.FirstOrDefaultAsync(u => u.Login == login || u.Email == login);

    public Task<User?> GetByIdAsync(Guid id) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id);

    public async Task CreateAsync(User user)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
}
