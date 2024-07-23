namespace Mp7251_Key_Reader
{
    public class PLCVariable
    {
        public string Name { get; set; } = "";
        public ushort NameSpaceIndex { get; set; } = 6;
    }

    public class PLCVariables
    {
        public PLCVariable KeySwitch { get; set; } = new PLCVariable() { Name = "::AsGlobalPV:KeySwitch" };
    }
}
