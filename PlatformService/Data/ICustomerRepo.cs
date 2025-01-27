using PlatformService.Models;

namespace PlatformService.Data
{
    public interface ICustomerRepo
    {
        Customer FindByEmail(string email);
    }
}