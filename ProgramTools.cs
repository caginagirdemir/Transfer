using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PointTracker
{


    [Serializable]
    public class AGVStatus
    {
        public UInt16 Id;
        public UInt16 SenderId;
        public UInt16 ReceiverId;
        public Byte MessageType;
        public Int16 DataLength;
        public UInt16 MachineId;
        public double X;
        public double Y;
        public double H;
        public Int16 Level;
        public byte PositionConfidence;
        public double SpeedNavigationPoint;
        public byte State;
        public double BatteryLevel;
        public sbyte AutoOrManual;
        public byte PositionInitialized;
        public Int32 LastSymbolPoint;
        public byte MachineAtLastSymbolPoint;
        public Int32 TargetSymbolPoint;
        public byte MachineAtTarget;
        public byte Operational;
        public byte InProduction;
        public byte LoadStatus;
        public double Batteryvoltage;
        public byte ChargingStatus;
    }

    [Serializable]
    public class move_info
    {
        public DateTime track_begin { get; set; }
        public UInt64 cycle_tick { get; set; }
        public double cycle_sum_speed { get; set; }
    }

    [Serializable]
    public class stop_info
    {
        public double pos_x { get; set; }
        public double pos_y { get; set; }
        public DateTime track_begin { get; set; }
    }

    [Serializable]
    public class state_info
    {
        public int state { get; set; }
        public DateTime track_begin { get; set; }
    }

    [Serializable]
    public class position_state_info
    {
        public double begin_pos_x { get; set; }
        public double begin_pos_y { get; set; }
        public double end_pos_x { get; set; }
        public double end_pos_y { get; set; }
        public DateTime track_begin { get; set; }
        public DateTime track_end { get; set; }
    }
}
