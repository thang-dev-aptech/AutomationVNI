import React from 'react'

export const Icon = ({ name, size = 18, color = 'currentColor', className = '', ...props }) => {
  const icons = {
    inbox: (
      <path
        d="M3 7V17C3 18.1046 3.89543 19 5 19H19C20.1046 19 21 17.1046 21 17V7M3 7L12 13L21 7M3 7C3 5.89543 3.89543 5 5 5H19C20.1046 5 21 5.89543 21 7"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    ),
    customers: (
      <>
        <path
          d="M17 21V19C17 17.9391 16.5786 16.9217 15.8284 16.1716C15.0783 15.4214 14.0609 15 13 15H5C3.93913 15 2.92172 15.4214 2.17157 16.1716C1.42143 16.9217 1 17.9391 1 19V21"
          stroke={color}
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
        <circle cx="9" cy="7" r="4" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
        <path
          d="M23 21V19C22.9986 18.1771 22.7388 17.3785 22.2573 16.7196C21.7758 16.0607 21.0967 15.5746 20.32 15.33"
          stroke={color}
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
        <path
          d="M16 3.13C16.8604 3.35031 17.623 3.85071 18.1676 4.55232C18.7122 5.25392 19.0078 6.11683 19.0078 7.005C19.0078 7.89317 18.7122 8.75608 18.1676 9.45768C17.623 10.1593 16.8604 10.6597 16 10.88"
          stroke={color}
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </>
    ),
    tasks: (
      <>
        <path d="M9 11L12 14L22 4" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
        <path
          d="M21 12V19C21 19.5304 20.7893 20.0391 20.4142 20.4142C20.0391 20.7893 19.5304 21 19 21H5C4.46957 21 3.96086 20.7893 3.58579 20.4142C3.21071 20.0391 3 19.5304 3 19V5C3 4.46957 3.21071 3.96086 3.58579 3.58579C3.96086 3.21071 4.46957 3 5 3H16"
          stroke={color}
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </>
    ),
    settings: (
      <>
        <circle cx="12" cy="12" r="3" stroke={color} strokeWidth="2" />
        <path
          d="M19.4 15A1.65 1.65 0 0 0 20 16.24L20.47 16.71A2 2 0 1 1 17.64 19.54L17.17 19.07A1.65 1.65 0 0 0 15.93 18.5A1.65 1.65 0 0 0 14.5 19.86V20.5A2 2 0 1 1 10.5 20.5V19.86A1.65 1.65 0 0 0 9.07 18.5A1.65 1.65 0 0 0 7.83 19.07L7.36 19.54A2 2 0 1 1 4.53 16.71L5 16.24A1.65 1.65 0 0 0 5.5 15A1.65 1.65 0 0 0 4.14 13.57H3.5A2 2 0 1 1 3.5 9.57H4.14A1.65 1.65 0 0 0 5.5 8.14A1.65 1.65 0 0 0 5 6.9L4.53 6.43A2 2 0 1 1 7.36 3.6L7.83 4.07A1.65 1.65 0 0 0 9.07 4.5A1.65 1.65 0 0 0 10.5 3.14V2.5A2 2 0 1 1 14.5 2.5V3.14A1.65 1.65 0 0 0 15.93 4.5A1.65 1.65 0 0 0 17.17 4.07L17.64 3.6A2 2 0 1 1 20.47 6.43L20 6.9A1.65 1.65 0 0 0 19.4 8.14A1.65 1.65 0 0 0 20.76 9.57H21.5A2 2 0 1 1 21.5 13.57H20.76A1.65 1.65 0 0 0 19.4 15Z"
          stroke={color}
          strokeWidth="2"
        />
      </>
    ),
    menu: (
      <path d="M3 12H21M3 6H21M3 18H21" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
    ),
    close: (
      <path d="M18 6L6 18M6 6L18 18" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
    ),
    logout: (
      <path
        d="M9 21H5C4.46957 21 3.96086 20.7893 3.58579 20.4142C3.21071 20.0391 3 19.5304 3 19V5C3 4.46957 3.21071 3.96086 3.58579 3.58579C3.96086 3.21071 4.46957 3 5 3H9M16 17L21 12M21 12L16 7M21 12H9"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    ),
    search: (
      <>
        <circle cx="11" cy="11" r="8" stroke={color} strokeWidth="2" />
        <path d="M21 21L16.65 16.65" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
      </>
    ),
    plus: (
      <path d="M12 5V19M5 12H19" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
    ),
    send: (
      <path d="M22 2L11 13M22 2L15 22L11 13M11 13L2 9L22 2" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
    ),
    user: (
      <>
        <path d="M20 21V19C20 17.9391 19.5786 16.9217 18.8284 16.1716C18.0783 15.4214 17.0609 15 16 15H8C6.93913 15 5.92172 15.4214 5.17157 16.1716C4.42143 16.9217 4 17.9391 4 19V21" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
        <circle cx="12" cy="7" r="4" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
      </>
    ),
    tag: (
      <path
        d="M20.59 13.41L13.42 20.58C13.2343 20.766 13.0137 20.9135 12.7709 21.0141C12.5281 21.1147 12.2678 21.1664 12.005 21.1664C11.7422 21.1664 11.4819 21.1147 11.2391 21.0141C10.9963 20.9135 10.7757 20.766 10.59 20.58L2 12V2H12L20.59 10.59C20.9625 10.9647 21.1716 11.4716 21.1716 12C21.1716 12.5284 20.9625 13.0353 20.59 13.41V13.41ZM7 7H7.01"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    ),
  }

  const svgContent = icons[name] || icons.inbox

  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      className={className}
      {...props}
    >
      {svgContent}
    </svg>
  )
}

export default Icon
