import { lazy } from 'react'

export const lazyPages = {
  login: lazy(() => import('../pages/LoginPage').then((module) => ({ default: module.LoginPage }))),
  setup: lazy(() => import('../pages/SetupPage').then((module) => ({ default: module.SetupPage }))),
  dashboard: lazy(() => import('../pages/DashboardPage').then((module) => ({ default: module.DashboardPage }))),
  missions: lazy(() => import('../pages/MissionsPage').then((module) => ({ default: module.MissionsPage }))),
  missionDetails: lazy(() => import('../pages/MissionDetailsPage').then((module) => ({ default: module.MissionDetailsPage }))),
  timeline: lazy(() => import('../pages/TimelinePage').then((module) => ({ default: module.TimelinePage }))),
  drivers: lazy(() => import('../pages/DriversPage').then((module) => ({ default: module.DriversPage }))),
  vehicles: lazy(() => import('../pages/VehiclesPage').then((module) => ({ default: module.VehiclesPage }))),
  maintenanceFleet: lazy(() => import('../pages/MaintenanceFleetPage').then((module) => ({ default: module.MaintenanceFleetPage }))),
  maintenanceVehicle: lazy(() => import('../pages/MaintenanceVehiclePage').then((module) => ({ default: module.MaintenanceVehiclePage }))),
  reports: lazy(() => import('../pages/ReportsPage').then((module) => ({ default: module.ReportsPage }))),
  sms: lazy(() => import('../pages/SmsPage').then((module) => ({ default: module.SmsPage }))),
  security: lazy(() => import('../pages/SecurityPage').then((module) => ({ default: module.SecurityPage }))),
  profile: lazy(() => import('../pages/ProfilePage').then((module) => ({ default: module.ProfilePage }))),
  settings: lazy(() => import('../pages/SettingsPage').then((module) => ({ default: module.SettingsPage }))),
}
