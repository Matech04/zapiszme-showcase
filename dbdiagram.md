Table EmployeeSchedule{
Id uuid [pk]
EmployeeId uuid [ref: > Employees.Id]
TenantId uuid [ref: > Tenants.Id]
ActiveFrom date
ActiveTo date
NumberOfCycles int

Note: "ActiveFrom and ActiveTo are represented by ValueObject DateRange"
}

Table ScheduleDay{
id uuid [pk]
EmployeeScheduleId uuid [ref: > EmployeeSchedule.Id]
DayIndex int
StartTime time
EndTime time
Note: "StartTime and EndTime are represented by ValueObject TimeRange"
}

Table ScheduleDayBreaks{
id uuid [pk]
SheduleDayId uuid [ref: > ScheduleDay.id]
StartTime time
EndTime time

Note: "StartTime and EndTime are represented by ValueObject TimeRange"
}

Table EmployeeScheduleOverride{
Id uuid [primary key]
EmployeeId uuid [ref: > Employees.Id]
TenantId uuid [ref: > Tenants.Id]
Date date
StartTime time
EndTime time
}

Table EmployeeScheduleOverrideBreaks{
id uuid [pk]
SheduleOverrideId uuid [ref: > EmployeeScheduleOverride.Id]
StartTime time
EndTime time

Note: "StartTime and EndTime are represented by ValueObject TimeRange"
}

Table EmployeeAbsence{
Id uuid
EmployeeId uuid [ref: > Employees.Id]
TenantId uuid [ref: > Tenants.Id]
StartDate date
EndDate date
AbsenceType AbsenceType
AbsenceStatus AbsenceStatus

Note: "StartDate and EndDate are represented by ValueObject DateRange"
}

Enum AbsenceType{
Vacation
}

Enum AbsenceStatus{
Approved
}
