using System.Linq;
using PlatformService.Models;

namespace PlatformService.Data
{
    public class CustomerRepo : ICustomerRepo
    {
        private readonly AppDbContext _context;

        public CustomerRepo(AppDbContext context)
        {
            _context = context;
        }

        public Customer FindByEmail(string email)
        {
            return _context.Customers.FirstOrDefault(p => p.Email == email);
        }
    }
}