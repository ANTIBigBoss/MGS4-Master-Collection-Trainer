using static MGS4_Master_Collection_Trainer.Constants;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>Generic memory inspection entry point for new MGS4 patterns.</summary>
    public sealed class DebugMethodManager
    {
        public static DebugMethodManager Instance { get; } = new DebugMethodManager();

        private DebugMethodManager()
        {
        }

        /// <summary>Reads an AOB-relative value with address information. True adds offset; false subtracts offset.</summary>
        public string ReadMemoryValue(string aobKey, int offset, bool forwardInMemory, int bytesToRead, DataType dataType)
        {
            return HelperMethods.Instance.ReadMemoryValue(aobKey, offset, forwardInMemory, bytesToRead, dataType);
        }
    }
}
