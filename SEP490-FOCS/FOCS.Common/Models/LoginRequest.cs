﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace FOCS.Common.Models
{
    public class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }

        public string StoreId {get; set; }
    }
}
