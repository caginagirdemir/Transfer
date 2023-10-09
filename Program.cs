using PointTracker.Db;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;

namespace PointTracker // Note: actual namespace depends on the project name.
{
    class Program
    {

        static Dictionary<UInt16, stop_info> stop_tracking = new Dictionary<UInt16, stop_info>();
        static Dictionary<UInt16, move_info> move_tracking = new Dictionary<UInt16, move_info>();
        static Dictionary<UInt16, state_info> state_tracking = new Dictionary<UInt16, state_info>();

        static Dictionary<UInt16, position_state_info> pos_conf_tracking = new Dictionary<UInt16, position_state_info>();
        static void tryConnect(ref TcpClient tcpClient)
        {
            try
            {
                //tcpClient.Connect("10.106.10.119", 8015); // ANKARA
                tcpClient.Connect("10.114.0.47", 8015); // BOLU
                //tcpClient.Connect("10.134.10.133", 8015); // Çayırova
                //tcpClient.Connect("127.0.0.1", 33477); // Local
            }
            catch (SocketException ex)
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.Write("Error : Connection Fail. Ex:");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                if (!tcpClient.Connected)
                {
                    tcpClient.Close();
                    Thread.Sleep(5000);
                    tryConnect(ref tcpClient); //recursion
                }
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine("Connected!");
                Console.ForegroundColor = ConsoleColor.White;
            }
        }


        static bool PointInsidePolygon((double x, double y) point, List<(double x, double y)> polygon)
        {
            double x = point.x, y = point.y;
            int n = polygon.Count;
            bool inside = false;

            double p1x = polygon[0].x, p1y = polygon[0].y;
            for (int i = 0; i < n + 1; i++)
            {
                double p2x = polygon[i % n].x, p2y = polygon[i % n].y;
                if (y > Math.Min(p1y, p2y))
                {
                    if (y <= Math.Max(p1y, p2y))
                    {
                        if (x <= Math.Max(p1x, p2x))
                        {
                            if (p1y != p2y)
                            {
                                double xIntersect = (y - p1y) * (p2x - p1x) / (p2y - p1y) + p1x;
                                if (p1x == p2x || x <= xIntersect)
                                {
                                    inside = !inside;
                                }
                            }
                        }
                    }
                }
                p1x = p2x;
                p1y = p2y;
            }

            return inside;
        }


        public static void Task_310(byte[] receiveBuffer, int buffer_pointer, ref dbContext db) //
        {

            //Parsing Section
            AGVStatus agvStatus = new AGVStatus();
            agvStatus.MachineId = BitConverter.ToUInt16(receiveBuffer, buffer_pointer);
            agvStatus.X = BitConverter.ToDouble(receiveBuffer, buffer_pointer + 2);
            agvStatus.Y = BitConverter.ToDouble(receiveBuffer, buffer_pointer + 10);
            agvStatus.H = BitConverter.ToDouble(receiveBuffer, buffer_pointer + 18);
            agvStatus.Level = BitConverter.ToInt16(receiveBuffer, buffer_pointer + 26);
            agvStatus.PositionConfidence = receiveBuffer[buffer_pointer + 28];
            agvStatus.SpeedNavigationPoint = BitConverter.ToDouble(receiveBuffer, buffer_pointer + 29);
            agvStatus.State = receiveBuffer[buffer_pointer + 37];
            agvStatus.BatteryLevel = BitConverter.ToDouble(receiveBuffer, buffer_pointer + 38);
            agvStatus.AutoOrManual = (sbyte)receiveBuffer[buffer_pointer + 46];
            agvStatus.PositionInitialized = receiveBuffer[buffer_pointer + 47];
            agvStatus.LastSymbolPoint = BitConverter.ToInt32(receiveBuffer, buffer_pointer + 48);
            agvStatus.MachineAtLastSymbolPoint = receiveBuffer[buffer_pointer + 52];
            agvStatus.TargetSymbolPoint = BitConverter.ToInt32(receiveBuffer, buffer_pointer + 53);
            agvStatus.MachineAtTarget = receiveBuffer[buffer_pointer + 57];
            agvStatus.Operational = receiveBuffer[buffer_pointer + 58];
            agvStatus.InProduction = receiveBuffer[buffer_pointer + 59];
            agvStatus.LoadStatus = receiveBuffer[buffer_pointer + 60];
            agvStatus.Batteryvoltage = BitConverter.ToDouble(receiveBuffer, buffer_pointer + 61);
            agvStatus.ChargingStatus = receiveBuffer[buffer_pointer + 69];

            if(agvStatus.X > 0 && agvStatus.Y > 0)
            {
                if(agvStatus.PositionConfidence < 70)
                {
                    if(!pos_conf_tracking.ContainsKey(agvStatus.MachineId))
                    {
                        pos_conf_tracking.Add(agvStatus.MachineId, new position_state_info
                        {
                            track_begin = DateTime.Now,
                            begin_pos_x = agvStatus.X,
                            begin_pos_y = agvStatus.Y,
                        });
                    }
                } else
                {
                    if (pos_conf_tracking.ContainsKey(agvStatus.MachineId))
                    {
                        db.PosConfs.Add(new PosConf
                        {
                            PosConfMachineId = agvStatus.MachineId,
                            PosConfBeginPosX = pos_conf_tracking[agvStatus.MachineId].begin_pos_x,
                            PosConfBeginPosY = pos_conf_tracking[agvStatus.MachineId].begin_pos_y,
                            PostConfBeginTime = pos_conf_tracking[agvStatus.MachineId].track_begin,
                            PosConfEndPosX = agvStatus.X,
                            PosConfEndPosY = agvStatus.Y,
                            PostConfEndTime = DateTime.Now,
                        });

                        pos_conf_tracking.Remove(agvStatus.MachineId);
                    }
                }

                if (agvStatus.SpeedNavigationPoint > 0.05)
                {
                    //stop record save
                    if(stop_tracking.ContainsKey(agvStatus.MachineId))
                    {
                        //Console.WriteLine(agvStatus.MachineId.ToString() + " Stop End");
                        db.Stops.AddAsync(new Stop
                        {
                            StopPointX = stop_tracking[agvStatus.MachineId].pos_x,
                            StopPointY = stop_tracking[agvStatus.MachineId].pos_y,
                            StopMachineId = agvStatus.MachineId,
                            StopBeginTime = stop_tracking[agvStatus.MachineId].track_begin,
                            StopEndTime = DateTime.Now
                        });
                        db.SaveChangesAsync();
                        stop_tracking.Remove(agvStatus.MachineId);
                    }

                    //move record start or update
                    if(!move_tracking.ContainsKey(agvStatus.MachineId))
                    {
                        move_tracking.Add(agvStatus.MachineId, new move_info
                        {
                            track_begin = DateTime.Now,
                            cycle_tick = 1,
                            cycle_sum_speed = agvStatus.SpeedNavigationPoint
                        });
                    } else
                    {
                        move_tracking[agvStatus.MachineId].cycle_tick++;
                        move_tracking[agvStatus.MachineId].cycle_sum_speed += agvStatus.SpeedNavigationPoint;
                    }
                } else
                {
                    //stop record start
                    if (!stop_tracking.ContainsKey(agvStatus.MachineId))
                    {
                        //Console.WriteLine(agvStatus.MachineId.ToString() + " Stop Start");
                        stop_tracking.Add(agvStatus.MachineId, new stop_info
                        {
                            pos_x = agvStatus.X,
                            pos_y = agvStatus.Y,
                            track_begin = DateTime.Now
                        });
                    }

                    //move record save
                    if(move_tracking.ContainsKey(agvStatus.MachineId))
                    {
                        double avg_speed = Math.Round(move_tracking[agvStatus.MachineId].cycle_sum_speed / move_tracking[agvStatus.MachineId].cycle_tick, 2);
                        db.Moves.AddAsync(new Move
                        {
                            MoveMachineId = agvStatus.MachineId,
                            MoveAvgSpeed = avg_speed,
                            MoveBeginTime = move_tracking[agvStatus.MachineId].track_begin,
                            MoveEndTime = DateTime.Now,
                        });
                        db.SaveChangesAsync();
                        move_tracking.Remove(agvStatus.MachineId);
                    }
                }




                if (state_tracking.ContainsKey(agvStatus.MachineId))
                {
                    //state change and record
                    if (state_tracking[agvStatus.MachineId].state != agvStatus.State)
                    {
                        db.AddAsync(new State
                        {
                            StateMachineId = agvStatus.MachineId,
                            StateBeginTime = state_tracking[agvStatus.MachineId].track_begin,
                            StateEndTime = DateTime.Now,
                            State1 = state_tracking[agvStatus.MachineId].state
                        });
                        db.SaveChangesAsync();
                        state_tracking.Remove(agvStatus.MachineId);

                    }
                }
                else if (agvStatus.State != 2 && agvStatus.State != 3 && agvStatus.State != 0) // state start
                {
                    state_tracking.Add(agvStatus.MachineId, new state_info
                    {
                        state = agvStatus.State,
                        track_begin = DateTime.Now
                    });
                }

            }
        }


        static void Main(string[] args)
        {
            //TCP Client Connection Section
            TcpClient client = new TcpClient();
            client.ReceiveBufferSize = 1048576;
            Console.WriteLine("TCP Connecting....");
            tryConnect(ref client);
            NetworkStream stream = client.GetStream();

            const int SenderId = 1040;


            int buffer_pointer = 0;
            int read_count = 0;
            int available_amount = 0;
            int message_id = 0;
            int remainBufferPointer = 0;
            int message_length = 0;

            byte[] receiveBuffer = new byte[client.ReceiveBufferSize];
            byte[] remainReceiveBuffer = new byte[client.ReceiveBufferSize];

            dbContext db = new dbContext();


            while (true)
            {

                while (client.Available < 300)
                {
                    System.Threading.Thread.Sleep(1000);
                    if (client.Available == 0)
                    {
                        if (!client.Connected)
                        {
                            client.Close();
                            Console.ForegroundColor = ConsoleColor.DarkYellow;
                            Console.WriteLine("Warning : Connection Problem. Trying connect again!");
                            Console.ForegroundColor = ConsoleColor.White;
                            tryConnect(ref client);
                            continue;
                        }
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine("Warning : Navithor didn't send anything within 1 second!");
                        Console.ForegroundColor = ConsoleColor.White;
                    }
                }
                available_amount = client.Available;
                //Console.WriteLine("Readed {0:D} Bytes.", available_amount);
                read_count = stream.Read(receiveBuffer, 0, available_amount);
                buffer_pointer = 0;
                if (remainBufferPointer > 0)
                {
                    // add receiveBuffer to end of the remainReceiveBuffer 
                    Buffer.BlockCopy(receiveBuffer, 0, remainReceiveBuffer, remainBufferPointer, read_count);
                    // move all data from remainReceiveBuffer to receiveBuffer
                    Buffer.BlockCopy(remainReceiveBuffer, 0, receiveBuffer, 0, read_count + remainBufferPointer);
                    // clear remain buffer pointer
                    read_count += remainBufferPointer;
                    remainBufferPointer = 0;
                }

                // Chunk process
                while ((read_count - buffer_pointer) > 0)
                {
                    if ((read_count - buffer_pointer) < 9) // check frame existence
                    {
                        remainBufferPointer = (read_count - buffer_pointer);
                        Buffer.BlockCopy(receiveBuffer, buffer_pointer, remainReceiveBuffer, 0, remainBufferPointer);
                        buffer_pointer = read_count;
                        break;
                    }

                    if (BitConverter.ToUInt16(receiveBuffer, buffer_pointer + 7) > (read_count - buffer_pointer - 9)) //message exist check
                    {
                        remainBufferPointer = (read_count - buffer_pointer);
                        Buffer.BlockCopy(receiveBuffer, buffer_pointer, remainReceiveBuffer, 0, remainBufferPointer);
                        buffer_pointer = read_count;
                        break;
                    }

                    message_id = BitConverter.ToUInt16(receiveBuffer, buffer_pointer);
                    message_length = (BitConverter.ToUInt16(receiveBuffer, buffer_pointer + 7)); //take message length before pass frame

                    buffer_pointer += 9; // PASS FRAME

                    //MESSAGE PARSING SECTION
                    switch (message_id)
                    {
                        case 310:
                            Task_310(receiveBuffer, buffer_pointer, ref db); //
                            break;
                        case 200:
                            if (receiveBuffer[buffer_pointer] > 0)
                                if (BitConverter.ToUInt16(receiveBuffer, buffer_pointer + 1) == 51)
                                    Console.WriteLine("Navithor 51 Msg Error->{0:D}", receiveBuffer[buffer_pointer]);
                            break;
                        case 203:
                            Byte[] HeartBeatFrame = new byte[13];
                            Byte[] _MsgId = BitConverter.GetBytes((UInt16)204); Buffer.BlockCopy(_MsgId, 0, HeartBeatFrame, 0, 2);
                            Byte[] _SenderId = BitConverter.GetBytes((UInt16)SenderId); Buffer.BlockCopy(_SenderId, 0, HeartBeatFrame, 2, 2);
                            Byte[] _ReceiverId = BitConverter.GetBytes((UInt16)1000); Buffer.BlockCopy(_ReceiverId, 0, HeartBeatFrame, 4, 2);
                            HeartBeatFrame[6] = 0x00;
                            Byte[] _DataLength = BitConverter.GetBytes((UInt16)0); Buffer.BlockCopy(_DataLength, 0, HeartBeatFrame, 7, 2);
                            stream.Write(HeartBeatFrame, 0, HeartBeatFrame.Length);
                            break;
                        default:
                            Console.WriteLine("Passed {0:D} Message!", message_id);
                            break;
                    }

                    buffer_pointer += message_length; // pass to other message
                }
            }

        }
    }
}