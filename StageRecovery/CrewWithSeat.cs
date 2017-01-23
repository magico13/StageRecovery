using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StageRecovery
{
    public class CrewWithSeat
    {
        public ProtoCrewMember CrewMember { get; private set; }
        public ProtoPartSnapshot PartSnapshot { get; private set; }

        public CrewWithSeat(ProtoCrewMember crew, ProtoPartSnapshot partSnapshot)
        {
            CrewMember = crew;
            PartSnapshot = partSnapshot;
        }

        public CrewWithSeat(ProtoCrewMember crew)
        {
            CrewMember = crew;
            PartSnapshot = crew?.seat?.part?.protoPartSnapshot;
        }

        public bool Restore(ProtoVessel vessel)
        {
            if (PartSnapshot == null)
            {
                return false;
            }
            ProtoPartSnapshot restoredPart = vessel.protoPartSnapshots.FirstOrDefault(p => p.craftID == PartSnapshot.craftID);
            if (restoredPart != null)
            {
                restoredPart.protoModuleCrew.Add(CrewMember);
                CrewMember.seat?.SpawnCrew();
                return true;
            }
            return false;
        }
    }
}
