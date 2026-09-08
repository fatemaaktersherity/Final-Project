using PharmacyV2.Models.SaleInvoice;
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.People
{
    public class Customer
    {
        public int CustomerId { get; set; }

        // ----- text -----
        [MaxLength(50)]
        public string? FirstName { get; set; }

        [MaxLength(50)]
        public string? LastName { get; set; }
        public string? Phone { get; set; } // UNIQUE — configured in Fluent API

        // ----- number -----
        [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "decimal(18,2)")]
        public decimal CreditLimit { get; set; } = 0;

        // ----- date -----
        public DateTime? DateOfBirth { get; set; }

        // ----- boolean -----
        public bool IsActive { get; set; } = true;

        // ----- image -----
        public byte[]? Photo { get; set; }

        [MaxLength(100)]
        public string? PhotoContentType { get; set; }

        // Public URL path to the physical file under wwwroot/images/customers,
        // e.g. "/images/customers/3_a1b2c3d4.jpg". Set by the dedicated
        // POST /api/customers/{id}/photo upload endpoint — same pattern as
        // Employee.PhotoPath.
        [MaxLength(500)]
        public string? PhotoPath { get; set; }

        // ----- relational data (dropdown) -----
        public int CustomerTypeId { get; set; }
        public CustomerType CustomerType { get; set; } = null!;

        // ----- details side of the master-details pair -----
        public ICollection<CustomerAddress> Addresses { get; set; } = new List<CustomerAddress>();

        public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    }
}