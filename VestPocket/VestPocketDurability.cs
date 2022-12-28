using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VestPocket
{
    /// <summary>
    /// Represents the different file durability strategies for VestPocketStore.
    /// These settings are only relevant when the store is was opened from a file.
    /// </summary>
    public enum VestPocketDurability
    {

        Unknown,

        /// <summary>
        /// The file is flushed everytime that the transaction queue is drained,
        /// but the OS file cache may delay the writes to disk. In this mode, changes
        /// may be lost if a power outage occurs after a transaction is processed before
        /// the OS decides to write the file cache to disk. This mode might offer improved
        /// performance for slower file systems.
        /// </summary>
        FileSystemCache,

        /// <summary>
        /// The file is flushed everytime that the transaction queue is drained,
        /// and a parameter is passed asking the OS to flush the file cache to disk. This
        /// method offers a good compromise between durability and performance. If a batch
        /// of transactions is being processed and a power outage occurs while the batch is
        /// being processed, then some transactions may not be flushed to disk.
        /// </summary>
        FlushOnDelay,

        /// <summary>
        /// The file is flushed everytime that a transaction completes, and a parameter is passed asking
        /// the OS to flush the file cache to disk. This is a fairly conservative option, and should
        /// offer the best durability at the cost of performance
        /// </summary>
        FlushEachTransaction

    }
}
