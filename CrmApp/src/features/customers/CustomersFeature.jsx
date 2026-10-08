import React from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import CustomerList from './components/CustomerList'
import CustomerProfile from './components/CustomerProfile'

// Hồ sơ khách theo URL (/customers/:id) để refresh và nút Back của trình duyệt đều chạy.
export const CustomersFeature = () => {
  const { id } = useParams()
  const navigate = useNavigate()

  return (
    <div>
      {id ? (
        <CustomerProfile
          key={id}
          customerId={id}
          onBack={() => navigate('/customers')}
          onCustomerUpdated={() => {}}
        />
      ) : (
        <CustomerList onSelectCustomer={(customerId) => navigate(`/customers/${customerId}`)} />
      )}
    </div>
  )
}

export default CustomersFeature
