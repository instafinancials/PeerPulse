using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PeerPulse.Web.Data;
//using PeerPulse.Web.Models.PeerDiscovery;

namespace PeerPulse.Web.Services
{
    public sealed class CompanyLookupService
    {
        private readonly PeerPulseDbContext _instaUsersDb;
        public CompanyLookupService(PeerPulseDbContext db)
        {
            _instaUsersDb = db;
        }
       
    }
}
