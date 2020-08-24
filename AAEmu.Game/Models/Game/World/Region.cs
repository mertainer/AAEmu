﻿using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Route;

using NLog;

namespace AAEmu.Game.Models.Game.World
{
    public class Region
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private readonly uint _worldId;
        private readonly object _objectsLock = new object();
        private GameObject[] _objects;
        private int _objectsSize, _charactersSize;

        public int X { get; }
        public int Y { get; }
        public int Id => Y + 1024 * X;

        public Region(uint worldId, int x, int y)
        {
            _worldId = worldId;
            X = x;
            Y = y;
        }

        public void AddObject(GameObject obj)
        {
            if (obj == null)
                return;
            lock (_objectsLock)
            {
                if (_objects == null)
                {
                    _objects = new GameObject[50];
                    _objectsSize = 0;
                }
                else if (_objectsSize >= _objects.Length)
                {
                    var temp = new GameObject[_objects.Length * 2];
                    Array.Copy(_objects, 0, temp, 0, _objectsSize);
                    _objects = temp;
                }

                _objects[_objectsSize] = obj;
                _objectsSize++;

                obj.Position.WorldId = _worldId;
                var zoneId = WorldManager.Instance.GetZoneId(_worldId, obj.Position.X, obj.Position.Y);
                if (zoneId > 0)
                    obj.Position.ZoneId = zoneId;

                if (obj is Character)
                    _charactersSize++;
            }
        }

        public void RemoveObject(GameObject obj) // TODO Нужно доделать =_+
        {
            if (obj == null)
                return;

            lock (_objectsLock)
            {
                if (_objects == null || _objectsSize == 0)
                    return;

                if (_objectsSize > 1)
                {
                    var index = -1;
                    for (var i = 0; i < _objects.Length; i++)
                        if (_objects[i] == obj)
                        {
                            index = i;
                            break;
                        }

                    if (index > -1)
                    {
                        _objects[index] = _objects[_objectsSize - 1];
                        _objects[_objectsSize - 1] = null;
                        _objectsSize--;
                    }
                }
                else if (_objectsSize == 1 && _objects[0] == obj)
                {
                    _objects[0] = null;
                    _objects = null;
                    _objectsSize = 0;
                }

                if (obj is Character)
                    _charactersSize--;
            }
        }

        public void AddToCharacters(GameObject obj)
        {
            if (_objects == null)
                return;

            // show the player all the facilities in the region
            if (obj is Character character)
            {
                var units = GetList(new List<Unit>(), obj.ObjId);
                foreach (var t in units)
                {
                    switch (t)
                    {
                        case Npc npc:
                            character.SendPacket(new SCUnitStatePacket(npc));
                            break;
                        case House house:
                            character.SendPacket(new SCUnitStatePacket(t));
                            character.SendPacket(new SCHouseStatePacket(house));
                            break;
                        case Slave slave:
                            slave.AddVisibleObject(character);
                            break;
                        case Gimmick gimmick:
                            // Gimmick move along the weaving shuttle in the Z-axis.
                            
                            gimmick.AddVisibleObject(character);
                            
                            Patrol patrol;
                            if (gimmick.Patrol == null)
                            {
                                var quill = new QuillZ { Interrupt = false, Loop = true, Abandon = false, Degree = 360 };
                                patrol = quill;
                                patrol.Pause(gimmick);
                                gimmick.Patrol = patrol;
                                gimmick.Patrol.LastPatrol = patrol;
                                patrol.Recovery(gimmick);
                                //gimmick.isRunning = true;
                            }
                            break;
                        default:
                            character.SendPacket(new SCUnitStatePacket(t));
                            break;
                    }
                }
                var doodads = GetList(new List<Doodad>(), obj.ObjId).ToArray();
                for (var i = 0; i < doodads.Length; i += 30)
                {
                    var count = doodads.Length - i;
                    var temp = new Doodad[count <= 30 ? count : 30];
                    Array.Copy(doodads, i, temp, 0, temp.Length);
                    character.SendPacket(new SCDoodadsCreatedPacket(temp));
                }
                // TODO ...others types...
                var gimmicks = GetList(new List<Gimmick>(), obj.ObjId).ToArray();
                for (var i = 0; i < gimmicks.Length; i += 30)
                {
                    var count = gimmicks.Length - i;
                    var temp = new Gimmick[count <= 30 ? count : 30];
                    Array.Copy(gimmicks, i, temp, 0, temp.Length);
                    character.SendPacket(new SCGimmicksCreatedPacket(temp));
                    temp = new Gimmick[0];
                    character.SendPacket(new SCGimmickJointsBrokenPacket(temp));

                }
            }
            // show the object to all players in the region
            foreach (var chr in GetList(new List<Character>(), obj.ObjId))
            {
                obj.AddVisibleObject(chr);
            }
        }

        public void RemoveFromCharacters(GameObject obj)
        {
            if (_objects == null)
                return;

            // remove all visible objects in the region from the player
            if (obj is Character character)
            {
                //var unitIds = GetListId<Unit>(new List<uint>(), obj.ObjId).ToArray();
                var unitIds = GetListId<Unit>(new List<uint>(), character.ObjId).ToArray();
                var units = GetList(new List<Unit>(), character.ObjId);
                foreach (var t in units)
                {
                    if (t is Npc npc)
                    {
                        // Stop NPCs that players don't see
                        npc.IsInPatrol = false;
                        npc.Patrol = null;
                    }
                }

                for (var offset = 0; offset < unitIds.Length; offset += 500)
                {
                    var length = unitIds.Length - offset;
                    var temp = new uint[length > 500 ? 500 : length];
                    Array.Copy(unitIds, offset, temp, 0, temp.Length);
                    character.SendPacket(new SCUnitsRemovedPacket(temp));
                }
                var doodadIds = GetListId<Doodad>(new List<uint>(), character.ObjId).ToArray();
                for (var offset = 0; offset < doodadIds.Length; offset += 400)
                {
                    var length = doodadIds.Length - offset;
                    var last = length <= 400;
                    var temp = new uint[last ? length : 400];
                    Array.Copy(doodadIds, offset, temp, 0, temp.Length);
                    character.SendPacket(new SCDoodadsRemovedPacket(last, temp));
                }
                // TODO ... others types...

                var gimmickIds = GetListId<Gimmick>(new List<uint>(), character.ObjId).ToArray();
                for (var offset = 0; offset < gimmickIds.Length; offset += 500)
                {
                    var length = gimmickIds.Length - offset;
                    var last = length <= 500;
                    var temp = new uint[last ? length : 500];
                    Array.Copy(gimmickIds, offset, temp, 0, temp.Length);
                    character.SendPacket(new SCGimmicksRemovedPacket(temp));
                }
            }

            // remove the object from all players in the region
            foreach (var chr in GetList(new List<Character>(), obj.ObjId))
            {
                obj.RemoveVisibleObject(chr);
            }
        }

        public Region[] GetNeighbors()
        {
            return WorldManager.Instance.GetNeighbors(_worldId, X, Y);
        }

        public bool AreNeighborsEmpty()
        {
            if (!IsEmpty())
                return false;
            foreach (var neighbor in GetNeighbors())
                if (!neighbor.IsEmpty())
                    return false;
            return true;
        }

        public bool IsEmpty()
        {
            return _charactersSize <= 0;
        }

        public List<uint> GetObjectIdsList(List<uint> result, uint exclude)
        {
            GameObject[] temp;
            lock (_objectsLock)
            {
                if (_objects == null || _objectsSize == 0)
                    return result;
                temp = new GameObject[_objectsSize];
                Array.Copy(_objects, 0, temp, 0, _objectsSize);
            }

            foreach (var obj in temp)
                if (obj.ObjId != exclude)
                    result.Add(obj.ObjId);
            return result;
        }

        public List<GameObject> GetObjectsList(List<GameObject> result, uint exclude)
        {
            GameObject[] temp;
            lock (_objectsLock)
            {
                if (_objects == null || _objectsSize == 0)
                    return result;
                temp = new GameObject[_objectsSize];
                Array.Copy(_objects, 0, temp, 0, _objectsSize);
            }

            foreach (var obj in temp)
                if (obj != null && obj.ObjId != exclude)
                    result.Add(obj);
            return result;
        }

        public List<uint> GetListId<T>(List<uint> result, uint exclude) where T : class
        {
            GameObject[] temp;
            lock (_objectsLock)
            {
                if (_objects == null || _objectsSize == 0)
                    return result;
                temp = new GameObject[_objectsSize];
                Array.Copy(_objects, 0, temp, 0, _objectsSize);
            }

            foreach (var obj in temp)
                if (obj is T && obj.ObjId != exclude)
                    result.Add(obj.ObjId);

            return result;
        }

        public List<T> GetList<T>(List<T> result, uint exclude) where T : class
        {
            GameObject[] temp;
            lock (_objectsLock)
            {
                if (_objects == null || _objectsSize == 0)
                    return result;
                temp = new GameObject[_objectsSize];
                Array.Copy(_objects, 0, temp, 0, _objectsSize);
            }

            foreach (var obj in temp)
            {
                var item = obj as T;
                if (item != null && obj.ObjId != exclude)
                    result.Add(item);
            }

            return result;
        }

        public List<T> GetList<T>(List<T> result, uint exclude, float x, float y, float sqrad) where T : class
        {
            GameObject[] temp;
            lock (_objectsLock)
            {
                if (_objects == null || _objectsSize == 0)
                    return result;
                temp = new GameObject[_objectsSize];
                Array.Copy(_objects, 0, temp, 0, _objectsSize);
            }

            foreach (var obj in temp)
            {
                var item = obj as T;
                if (item == null || obj.ObjId == exclude)
                    continue;
                var dx = obj.Position.X - x;
                dx *= dx;
                if (dx > sqrad)
                    continue;
                var dy = obj.Position.Y - y;
                dy *= dy;
                if (dx + dy < sqrad)
                    result.Add(item);
            }

            return result;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;
            if (obj.GetType() != typeof(Region))
                return false;
            var other = (Region)obj;
            return other._worldId == _worldId && other.X == X && other.Y == Y;
        }

        public override int GetHashCode()
        {
            var result = (int)_worldId;
            result = (result * 397) ^ X;
            result = (result * 397) ^ Y;
            return result;
        }
    }
}
