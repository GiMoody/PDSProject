using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDSProject {
    class RejectedFileException : Exception {
        public RejectedFileException() {

        }

        public RejectedFileException(string message) : base(message) {

        }

        public RejectedFileException(string message, Exception inner) : base(message, inner) {

        }
    }
}
