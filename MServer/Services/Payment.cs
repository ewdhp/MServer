using System;
using MServer.Data;
using MServer.Models;
namespace MServer.Services
{
    public interface IPay
    {
        public IPayResponse Process(Loan data);
    }

    public interface IPayResponse
    {
        public bool Success { get; set; }
        public Loan Data { get; set; }
    }

    public class PayService
    (
        UDbContext dbContext,
        IUCService user)
    {
        protected readonly IUCService _user = user ??
        throw new ArgumentNullException(nameof(user));

        protected readonly UDbContext _dbContext = dbContext ??
        throw new ArgumentNullException(nameof(dbContext));
    }

    public class PayOnline
    (
        IUCService u, UDbContext c) :
        PayService(c, u),
            IPay
    {
        public IPayResponse Process(Loan data)
        {
            throw new NotImplementedException();
        }
    }

    public class PayInStore
    (IUCService u, UDbContext c) :
        PayService(c, u),
            IPay
    {
        public IPayResponse Process(Loan data)
        {
            throw new NotImplementedException();
        }
    }
}