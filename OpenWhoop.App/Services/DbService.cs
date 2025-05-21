using System;
using Microsoft.EntityFrameworkCore;
using OpenWhoop.Core.Data;

namespace OpenWhoop.App.Services
{
    public class DbService : IDisposable, IAsyncDisposable
    {
        private readonly AppDbContext _context;

        public DbService(AppDbContext context)
        {
            _context = context;
        }

        public AppDbContext Context => _context;

        public void Migrate()
        {
            _context.Database.Migrate();
        }

        public void Dispose()
        {
            _context.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
        }
    }
}
