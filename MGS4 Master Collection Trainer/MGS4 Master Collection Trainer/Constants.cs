namespace MGS4_Master_Collection_Trainer
{
    public static class Constants
    {
        // Process.GetProcessesByName expects the name without the .exe extension.
        public const string PROCESS_NAME = "mgs4";
        public const string PROCESS_MODULE_NAME = "mgs4.exe";

        public const string PlayerHookAobKey = "aPlayer";
        public const int PlayerHookOverwriteLength = 7;
        public const int PlayerHookAllocationSize = 128;
        public const int PlayerHookPointerOffset = 64;

        public const string StressAobKey = "aStress";

        public enum DataType
        {
            UInt8,
            Int8,
            Int16,
            UInt16,
            Int32,
            UInt32,
            Float,
            Int64,
            UInt64,
            Double,
            ByteArray,
            String
        }

        // Add verified MGS4 module and pointer offsets here.

        // Add verified MGS4 value offsets here.
    }
}
