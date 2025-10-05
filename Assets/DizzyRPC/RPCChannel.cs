using System;
using System.Text;
using DizzyRPC.Debugger;
using DizzyRPC.Examples;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;

namespace DizzyRPC
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class RPCChannel : UdonSharpBehaviour
    {
        private const int HEADER_OFFSET_TARGET = 0;
        private const int HEADER_LENGTH_TARGET = sizeof(ushort);
        private const int HEADER_OFFSET_ID = HEADER_OFFSET_TARGET + HEADER_LENGTH_TARGET;
        private const int HEADER_LENGTH_ID = sizeof(ushort);
        private const int HEADER_OFFSET_LENGTH = HEADER_OFFSET_ID + HEADER_LENGTH_ID;
        private const int HEADER_LENGTH_LENGTH = sizeof(ushort);
        private const int HEADER_LENGTH = HEADER_OFFSET_LENGTH + HEADER_LENGTH_LENGTH;

        [SerializeField] private RPCDebugger debugger;
        private VRCPlayerApi localPlayer;

        [UdonSynced, HideInInspector]
        public byte[] rpcData = Array.Empty<byte>();

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
        }

        public void SendEvent(ushort id, params object[] parameters)
        {
            if (debugger != null) debugger._OnSendRPC(Networking.GetOwner(gameObject), id, parameters);
            switch (parameters.Length)
            {
                case 0:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}");
                    return;
                case 1:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}", parameters[0]);
                    return;
                case 2:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}", parameters[0], parameters[1]);
                    return;
                case 3:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}", parameters[0], parameters[1], parameters[2]);
                    return;
                case 4:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}", parameters[0], parameters[1], parameters[2], parameters[3]);
                    return;
                case 5:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}", parameters[0], parameters[1], parameters[2], parameters[3], parameters[4]);
                    return;
                case 6:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}", parameters[0], parameters[1], parameters[2], parameters[3], parameters[4], parameters[5]);
                    return;
                case 7:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}", parameters[0], parameters[1], parameters[2], parameters[3], parameters[4], parameters[5], parameters[6]);
                    return;
                case 8:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}", parameters[0], parameters[1], parameters[2], parameters[3], parameters[4], parameters[5], parameters[6], parameters[7]);
                    return;
            }

            Debug.LogError($"[DizzyRPC] Skipping RPC {id} because it had too many parameters!");
        }

        public void SendVariable(VRCPlayerApi target, ushort id, bool ignoreDuplicates, params object[] parameters)
        {
            if (ignoreDuplicates)
            {
                for (int i = 0; i < rpcData.Length;)
                {
                    if (rpcData.Length < i + HEADER_LENGTH) break;
                    ushort _id = BitConverter.ToUInt16(rpcData, HEADER_OFFSET_ID);
                    ushort length = BitConverter.ToUInt16(rpcData, i + HEADER_OFFSET_LENGTH);

                    if (_id == id)
                    {
                        rpcData = Combine(Take(rpcData, i), Trim(rpcData, i+length+HEADER_LENGTH)); // Remove that RPC, so that the current newer one gets added on the end
                        break;
                    }

                    if (rpcData.Length < length + HEADER_LENGTH) break;
                    i += length + HEADER_LENGTH;
                }
            }

            var rpcBytes = new byte[0];
            foreach (var param in parameters)
            {
                var paramBytes = Encode(param);
                rpcBytes = Combine(rpcBytes, paramBytes);

                string paramData = "";
                foreach (var b in paramBytes) paramData += $" {b}";
                // Debug.Log($"Encoded RPC Parameter {param} of type {param.GetType().FullName} into {paramBytes.Length} bytes: [{paramData.Trim()}]");
            }

            rpcBytes = Combine(
                Encode(target == null ? ushort.MaxValue : (ushort)target.playerId),
                Encode(id),
                Encode((ushort)rpcBytes.Length),
                rpcBytes
            );

            string data = "";
            foreach (var b in rpcBytes) data += $" {b}";
            // Debug.Log($"Appending RPC Data: {rpcBytes.Length} - [{data.Trim()}] (Should be index {rpcIndex}, length {rpcBytes.Length - 16}, player {(target == null ? -1 : target.playerId)}, id {id}, parameters)");

            rpcData = Combine(rpcData, rpcBytes);
            RequestSerialization();
        }

        private byte[] Combine(byte[] baseArray, params byte[] append)
        {
            var newArray = new byte[baseArray.Length + append.Length];
            Buffer.BlockCopy(baseArray, 0, newArray, 0, baseArray.Length);
            Buffer.BlockCopy(append, 0, newArray, baseArray.Length, append.Length);
            return newArray;
        }

        private byte[] Combine(byte[] baseArray, params byte[][] append)
        {
            foreach (var arr in append) baseArray = Combine(baseArray, arr);
            return baseArray;
        }

        private byte[] Trim(byte[] baseArray, int numBytes)
        {
            if (numBytes > baseArray.Length)
            {
                Debug.LogError($"Cannot trim {numBytes} from an array only containing {baseArray.Length}!");
                return null;
            }

            var newArray = new byte[baseArray.Length - numBytes];
            Buffer.BlockCopy(baseArray, numBytes, newArray, 0, newArray.Length);
            return newArray;
        }

        private byte[] Take(byte[] baseArray, int numBytes)
        {
            if (numBytes > baseArray.Length)
            {
                Debug.LogError($"Cannot take {numBytes} from an array only containing {baseArray.Length}!");
                return null;
            }

            var newArray = new byte[numBytes];
            Buffer.BlockCopy(baseArray, 0, newArray, 0, numBytes);
            return newArray;
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            if (debugger != null) debugger.OnVariableSyncSent(result);
            if (result.success)
            {
                bool hadAnyData = rpcData.Length > 0;
                rpcData = new byte[0];
                if (hadAnyData) RequestSerialization();
            }
        }

        public override void OnDeserialization(DeserializationResult result)
        {
            int rpcCount = 0;

            // string data = "";
            // foreach (var b in rpcData) data += $" {b}";
            // Debug.Log($"Received RPC Data: {rpcData.Length} - [{data.Trim()}]");

            while (rpcData.Length > 0)
            {
                if (rpcData.Length < HEADER_LENGTH)
                {
                    Panic($"RPC header is malformed!");
                    return;
                }

                ushort length = BitConverter.ToUInt16(rpcData, HEADER_OFFSET_LENGTH);
                int targetPlayerId = BitConverter.ToUInt16(rpcData, HEADER_OFFSET_TARGET);
                ushort id = BitConverter.ToUInt16(rpcData, HEADER_OFFSET_ID);

                if (rpcData.Length < length + HEADER_LENGTH)
                {
                    Panic($"RPC data is incomplete! Expected at least {length + HEADER_LENGTH} bytes, found {rpcData.Length}!");
                    return;
                }

                var rpcBytes = Take(rpcData, length + HEADER_LENGTH);
                rpcData = Trim(rpcData, length + HEADER_LENGTH);

                if (targetPlayerId != ushort.MaxValue && targetPlayerId != localPlayer.playerId) continue; // This RPC isn't for us

                _DecodeRPC(id, Trim(rpcBytes, HEADER_LENGTH));
                rpcCount++;
            }

            if (debugger != null) debugger.OnVariableSyncReceived(Networking.GetOwner(gameObject), result.sendTime, result.receiveTime, rpcCount);
        }

        private void Panic(string message)
        {
            Debug.LogError($"RPC data was corrupted! Dropping ALL pending RPCs. ({message})");
            rpcData = new byte[0];
        }

        private byte[] Encode(object o)
        {
            // Debug.Log($"Encoding type: {o.GetType().FullName}");
            var type = o.GetType();
            if (type == typeof(bool)) return BitConverter.GetBytes((bool)o);
            if (type == typeof(sbyte)) return new[] { (byte)(sbyte)o };
            if (type == typeof(byte)) return new[] { (byte)o };
            if (type == typeof(short)) return BitConverter.GetBytes((short)o);
            if (type == typeof(ushort)) return BitConverter.GetBytes((ushort)o);
            if (type == typeof(int)) return BitConverter.GetBytes((int)o);
            if (type == typeof(uint)) return BitConverter.GetBytes((uint)o);
            if (type == typeof(long)) return BitConverter.GetBytes((long)o);
            if (type == typeof(ulong)) return BitConverter.GetBytes((ulong)o);
            if (type == typeof(float)) return BitConverter.GetBytes((float)o);
            if (type == typeof(double)) return BitConverter.GetBytes((double)o);
            if (type == typeof(char)) return BitConverter.GetBytes((char)o);
            if (type == typeof(string))
            {
                string s = (string)o;
                return Combine(BitConverter.GetBytes(s.Length), Encoding.UTF8.GetBytes(s));
            }
            if (type == typeof(Vector2))
            {
                var vec = (Vector2)o;
                var bytes = new byte[4 * 2];
                Buffer.BlockCopy(BitConverter.GetBytes(vec.x), 0, bytes, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(vec.y), 0, bytes, 4, 4);
                return bytes;
            }
            if (type == typeof(Vector3))
            {
                var vec = (Vector3)o;
                var bytes = new byte[4 * 3];
                Buffer.BlockCopy(BitConverter.GetBytes(vec.x), 0, bytes, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(vec.y), 0, bytes, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(vec.z), 0, bytes, 4 * 2, 4);
                return bytes;
            }
            if (type == typeof(Vector4))
            {
                var vec = (Vector4)o;
                var bytes = new byte[4 * 4];
                Buffer.BlockCopy(BitConverter.GetBytes(vec.x), 0, bytes, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(vec.y), 0, bytes, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(vec.z), 0, bytes, 4 * 2, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(vec.w), 0, bytes, 4 * 3, 4);
                return bytes;
            }
            if (type == typeof(Quaternion))
            {
                var q = (Quaternion)o;
                var bytes = new byte[4 * 4];
                Buffer.BlockCopy(BitConverter.GetBytes(q.x), 0, bytes, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(q.y), 0, bytes, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(q.z), 0, bytes, 4 * 2, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(q.w), 0, bytes, 4 * 3, 4);
                return bytes;
            }
            if (type == typeof(Color))
            {
                var c = (Color)o;
                var bytes = new byte[4 * 4];
                Buffer.BlockCopy(BitConverter.GetBytes(c.r), 0, bytes, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(c.g), 0, bytes, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(c.b), 0, bytes, 4 * 2, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(c.a), 0, bytes, 4 * 3, 4);
                return bytes;
            }
            if (type == typeof(Color32))
            {
                var c = (Color32)o;
                return new[] { c.r, c.g, c.b, c.a };
            }

            if (type == typeof(bool[]))
            {
                var arr = (bool[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                int bits = 0;
                byte currentByte = 0;
                for (int i = arr.Length - 1; i >= 0; i--)
                {
                    var b = arr[i];
                    currentByte |= b ? (byte)0b1 : (byte)0b0;
                    if (++bits == 8)
                    {
                        arrayBytes = Combine(arrayBytes, currentByte);
                        currentByte = 0;
                        bits = 0;
                    }
                    currentByte = (byte)(currentByte << 1);
                }
                if(bits>0) arrayBytes = Combine(arrayBytes, currentByte);
                return arrayBytes;
            }

            if (type == typeof(sbyte[]))
            {
                var arr = (sbyte[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(byte[]))
            {
                var arr = (byte[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(short[]))
            {
                var arr = (short[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(ushort[])) 
            {
                var arr = (ushort[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(int[]))
            {
                var arr = (int[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(uint[])) 
            {
                var arr = (uint[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(long[])) 
            {
                var arr = (long[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(ulong[])) 
            {
                var arr = (ulong[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(float[])) 
            {
                var arr = (float[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(double[])) 
            {
                var arr = (double[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(char[])) 
            {
                var arr = (char[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(string[]))
            {
                var arr = (string[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(Vector2[]))
            {
                var arr = (Vector2[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(Vector3[]))
            {
                var arr = (Vector3[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(Vector4[]))
            {
                var arr = (Vector4[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(Quaternion[]))
            {
                var arr = (Quaternion[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(Color[]))
            {
                var arr = (Color[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }
            if (type == typeof(Color32[]))
            {
                var arr = (Color32[])o;
                var arrayBytes = BitConverter.GetBytes(arr.Length);
                foreach (var val in arr) arrayBytes = Combine(arrayBytes, Encode(val));
            }

            Debug.LogError($"Could not encode type: {o.GetType().FullName}");
            return null;
        }

        #region Decode

        private bool DecodeBoolean(byte[] bytes, ref int position)
        {
            bool b = BitConverter.ToBoolean(bytes, position);
            position++;
            return b;
        }

        private sbyte DecodeSByte(byte[] bytes, ref int position)
        {
            sbyte b = (sbyte)bytes[position];
            position += 1;
            return b;
        }

        private byte DecodeByte(byte[] bytes, ref int position)
        {
            byte b = bytes[position];
            position += 1;
            return b;
        }

        private short DecodeInt16(byte[] bytes, ref int position)
        {
            short s = BitConverter.ToInt16(bytes, position);
            position += 2;
            return s;
        }

        private ushort DecodeUInt16(byte[] bytes, ref int position)
        {
            ushort us = BitConverter.ToUInt16(bytes, position);
            position += 2;
            return us;
        }

        private int DecodeInt32(byte[] bytes, ref int position)
        {
            int i = BitConverter.ToInt32(bytes, position);
            position += 4;
            return i;
        }

        private uint DecodeUInt32(byte[] bytes, ref int position)
        {
            uint ui = BitConverter.ToUInt32(bytes, position);
            position += 4;
            return ui;
        }

        private long DecodeInt64(byte[] bytes, ref int position)
        {
            long l = BitConverter.ToInt64(bytes, position);
            position += 8;
            return l;
        }

        private ulong DecodeUInt64(byte[] bytes, ref int position)
        {
            ulong ul = BitConverter.ToUInt64(bytes, position);
            position += 8;
            return ul;
        }

        private float DecodeSingle(byte[] bytes, ref int position)
        {
            float f = BitConverter.ToSingle(bytes, position);
            position += 4;
            return f;
        }

        private double DecodeDouble(byte[] bytes, ref int position)
        {
            double d = BitConverter.ToDouble(bytes, position);
            position += 8;
            return d;
        }

        private char DecodeChar(byte[] bytes, ref int position)
        {
            char c = BitConverter.ToChar(bytes, position);
            position += 2;
            return c;
        }

        private Vector2 DecodeVector2(byte[] bytes, ref int position)
        {
            float x = BitConverter.ToSingle(bytes, position);
            float y = BitConverter.ToSingle(bytes, position + 4);
            position += 4 * 2;
            return new Vector2(x, y);
        }

        private Vector3 DecodeVector3(byte[] bytes, ref int position)
        {
            float x = BitConverter.ToSingle(bytes, position);
            float y = BitConverter.ToSingle(bytes, position + 4);
            float z = BitConverter.ToSingle(bytes, position + 8);
            position += 4 * 3;
            return new Vector3(x, y, z);
        }

        private Vector4 DecodeVector4(byte[] bytes, ref int position)
        {
            float x = BitConverter.ToSingle(bytes, position);
            float y = BitConverter.ToSingle(bytes, position + 4);
            float z = BitConverter.ToSingle(bytes, position + 8);
            float w = BitConverter.ToSingle(bytes, position + 12);
            position += 4 * 4;
            return new Vector4(x, y, z, w);
        }

        private Quaternion DecodeQuaternion(byte[] bytes, ref int position)
        {
            float x = BitConverter.ToSingle(bytes, position);
            float y = BitConverter.ToSingle(bytes, position + 4);
            float z = BitConverter.ToSingle(bytes, position + 8);
            float w = BitConverter.ToSingle(bytes, position + 12);
            position += 4 * 4;
            return new Quaternion(x, y, z, w);
        }

        private Color DecodeColor(byte[] bytes, ref int position)
        {
            float r = BitConverter.ToSingle(bytes, position);
            float g = BitConverter.ToSingle(bytes, position + 4);
            float b = BitConverter.ToSingle(bytes, position + 8);
            float a = BitConverter.ToSingle(bytes, position + 12);
            position += 4 * 4;
            return new Color(r, g, b, a);
        }

        private Color32 DecodeColor32(byte[] bytes, ref int position)
        {
            byte r = bytes[position];
            byte g = bytes[position + 1];
            byte b = bytes[position + 2];
            byte a = bytes[position + 3];
            position += 4;
            return new Color32(r, g, b, a);
        }

        private string DecodeString(byte[] bytes, ref int position)
        {
            int length = DecodeInt32(bytes, ref position);
            string s = Encoding.UTF8.GetString(bytes, position, length);
            position += length;
            return s;
        }

        #endregion
        #region Decode (Arrays)

        private bool[] DecodeBooleanArray(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            bool[] array = new bool[count];
            int i = 0;
            for(int _byte = 0; i<count&& _byte<count/8+Math.Sign(count%8); _byte++)
            {
                byte b = DecodeByte(bytes, ref position);
                for (int bit = 0; i < count && bit < 8; bit++)
                {
                    array[i++] = (b & 0b1)==0b1;
                    b = (byte)(b >> 1);
                }
            }
            return array;
        }

        private sbyte[] DecodeSByteArray(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            sbyte[] array = new sbyte[count];
            for (int i = 0; i < count; i++) array[i] = DecodeSByte(bytes, ref position);
            return array;
        }

        private byte[] DecodeByteArray(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            byte[] array = new byte[count];
            for (int i = 0; i < count; i++) array[i] = DecodeByte(bytes, ref position);
            return array;
        }

        private short[] DecodeInt16Array(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            short[] array = new short[count];
            for (int i = 0; i < count; i++) array[i] = DecodeInt16(bytes, ref position);
            return array;
        }

        private ushort[] DecodeUInt16Array(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            ushort[] array = new ushort[count];
            for (int i = 0; i < count; i++) array[i] = DecodeUInt16(bytes, ref position);
            return array;
        }

        private int[] DecodeInt32Array(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            int[] array = new int[count];
            for (int i = 0; i < count; i++) array[i] = DecodeInt32(bytes, ref position);
            return array;
        }

        private uint[] DecodeUInt32Array(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            uint[] array = new uint[count];
            for (int i = 0; i < count; i++) array[i] = DecodeUInt32(bytes, ref position);
            return array;
        }

        private long[] DecodeInt64Array(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            long[] array = new long[count];
            for (int i = 0; i < count; i++) array[i] = DecodeInt64(bytes, ref position);
            return array;
        }

        private ulong[] DecodeUInt64Array(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            ulong[] array = new ulong[count];
            for (int i = 0; i < count; i++) array[i] = DecodeUInt64(bytes, ref position);
            return array;
        }

        private float[] DecodeSingleArray(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            float[] array = new float[count];
            for (int i = 0; i < count; i++) array[i] = DecodeSingle(bytes, ref position);
            return array;
        }

        private double[] DecodeDoubleArray(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            double[] array = new double[count];
            for (int i = 0; i < count; i++) array[i] = DecodeDouble(bytes, ref position);
            return array;
        }

        private char[] DecodeCharArray(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            char[] array = new char[count];
            for (int i = 0; i < count; i++) array[i] = DecodeChar(bytes, ref position);
            return array;
        }

        private Vector2[] DecodeVector2Array(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            Vector2[] array = new Vector2[count];
            for (int i = 0; i < count; i++) array[i] = DecodeVector2(bytes, ref position);
            return array;
        }

        private Vector3[] DecodeVector3Array(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            Vector3[] array = new Vector3[count];
            for (int i = 0; i < count; i++) array[i] = DecodeVector3(bytes, ref position);
            return array;
        }

        private Vector4[] DecodeVector4Array(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            Vector4[] array = new Vector4[count];
            for (int i = 0; i < count; i++) array[i] = DecodeVector4(bytes, ref position);
            return array;
        }

        private Quaternion[] DecodeQuaternionArray(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            Quaternion[] array = new Quaternion[count];
            for (int i = 0; i < count; i++) array[i] = DecodeQuaternion(bytes, ref position);
            return array;
        }

        private Color[] DecodeColorArray(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            Color[] array = new Color[count];
            for (int i = 0; i < count; i++) array[i] = DecodeColor(bytes, ref position);
            return array;
        }

        private Color32[] DecodeColor32Array(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            Color32[] array = new Color32[count];
            for (int i = 0; i < count; i++) array[i] = DecodeColor32(bytes, ref position);
            return array;
        }

        private string[] DecodeStringArray(byte[] bytes, ref int position)
        {
            int count = DecodeInt32(bytes, ref position);
            string[] array = new string[count];
            for (int i = 0; i < count; i++) array[i] = DecodeString(bytes, ref position);
            return array;
        }

        #endregion
        #region Generated RPCs (DO NOT EDIT)
        public const int RPC_RoutedRPCExample__SomeRPC = 0;
        public const int RPC_SingletonRPCExample__Example = 1;
        public const int RPC_SingletonRPCExample__ExampleWithParameters = 2;
        public const int RPC_SingletonRPCExample__ExampleVariableRPC = 3;
        public const int RPC_GraphRPCExample__asdf = 4;
        public const int RPC_RoutedGraphRPCExample__asdf = 5;
        public const int RPC_SharpRoutedGraphRPCExample_NewRPCMethod = 6;
        
        private void _DecodeRPC(int id, byte[] data) {}
        #endregion
    }
}