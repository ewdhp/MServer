using Newtonsoft.Json.Serialization;

namespace MServer.Models
{
    public class PhaseRequest
    {
        public string Type { get; set; }
        public Loan LoanData { get; set; }
        public decimal Amount { get; set; } = 0.0M;
        public object Data { get; set; }
        public string payMethod { get; set; }
    }

    public class PhaseResponse
    {
        public bool Success { get; set; }
        public string Msg { get; set; }
        public string Component { get; set; }
        public Loan LoanData { get; set; }
    }
}